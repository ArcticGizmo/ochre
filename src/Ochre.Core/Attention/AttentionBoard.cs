using System.Text.Json.Nodes;
using Ochre.Core.Claude;
using Ochre.Core.Claude.Control;
using Ochre.Core.Sessions;

namespace Ochre.Core.Attention;

internal enum AttentionKind
{
    /// <summary>A controlled agent asks to use a tool.</summary>
    Permission,

    /// <summary>A controlled agent asks the user a question (<c>AskUserQuestion</c>).</summary>
    Question,

    /// <summary>A controlled agent presents a plan to approve (<c>ExitPlanMode</c>).</summary>
    Plan,

    /// <summary>A controlled agent waits on something only another client sees (a Remote Control prompt).</summary>
    Elsewhere,

    /// <summary>A session outside Ochre is waiting; it's answered where it runs.</summary>
    Waiting,

    /// <summary>A controlled agent's turn ended and the user hasn't looked since.</summary>
    Finished,

    /// <summary>A controlled agent's turn ended in an error, or its process exited with one.</summary>
    Failed,
}

/// <param name="Id">Names this item only while it lasts; an action on an id the board no longer lists is refused.</param>
/// <param name="RequestId">The pending prompt's id, for answering it through the session.</param>
internal sealed record AttentionItem(
    string Id,
    string SessionId,
    string TaskId,
    AttentionKind Kind,
    string Title,
    string Detail,
    DateTimeOffset Since,
    string? RequestId,
    string? Role,
    string Cwd)
{
    /// <summary>Something is blocked on the user, as opposed to done and waiting to be read.</summary>
    public bool NeedsYou => Kind is not (AttentionKind.Finished or AttentionKind.Failed);
}

/// <summary>
/// Everything that needs the user, derived from the sessions each time they change (ADR 0013). Only the user's own
/// marks are kept here (which finished turns they've seen, which items they dismissed), in memory, so the board
/// can never disagree with the sessions it reads.
/// </summary>
internal sealed class AttentionBoard : IDisposable
{
    private const int MaxDetail = 200;

    private readonly SessionManager _sessions;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private IReadOnlyList<AttentionItem> _items = [];
    private long _nextId;

    public AttentionBoard(SessionManager sessions, TimeProvider? clock = null)
    {
        _sessions = sessions;
        _clock = clock ?? TimeProvider.System;
        _sessions.Changed += OnSessionChanged;
        _sessions.ListChanged += Refresh;
    }

    /// <summary>The items changed. Raised outside the board's lock, from whichever thread noticed.</summary>
    public event Action? Changed;

    /// <summary>Grouped by task; groups with something blocked first, oldest first; within a group, blocked before
    /// done, oldest first.</summary>
    public IReadOnlyList<AttentionItem> Items
    {
        get
        {
            lock (_gate) return _items;
        }
    }

    public AttentionItem? Get(string itemId)
    {
        lock (_gate) return _items.FirstOrDefault(i => i.Id == itemId);
    }

    /// <summary>The user has a session's thread or pane in front of them: its finished turn is read. False when it
    /// had none.</summary>
    public bool Seen(string sessionId)
    {
        lock (_gate)
        {
            if (!_tracks.TryGetValue(sessionId, out var track) || track.Finish is not { Cleared: false } finish) return false;
            finish.Cleared = true;
        }
        Refresh();
        return true;
    }

    /// <summary>Drops an item the user can't answer from Ochre (finished, failed, waiting elsewhere or outside) until
    /// its session's state changes again. False for an id the board doesn't list or a prompt that must be
    /// answered.</summary>
    public bool Dismiss(string itemId)
    {
        lock (_gate)
        {
            if (_items.FirstOrDefault(i => i.Id == itemId) is not { } item || !_tracks.TryGetValue(item.SessionId, out var track)) return false;
            switch (item.Kind)
            {
                case AttentionKind.Finished or AttentionKind.Failed when track.Finish is { } finish:
                    finish.Cleared = true;
                    break;
                case AttentionKind.Waiting or AttentionKind.Elsewhere when track.Need is { } need:
                    track.DismissedNeed = need.Key;
                    break;
                default:
                    return false;
            }
        }
        Refresh();
        return true;
    }

    private void OnSessionChanged(LiveSession session, SessionChanges changes) => Refresh();

    /// <summary>Reads every session again and raises <see cref="Changed"/> if the items differ.</summary>
    public void Refresh()
    {
        bool changed;
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var all = _sessions.All;
            var items = new List<AttentionItem>();
            foreach (var session in all)
            {
                if (!_tracks.TryGetValue(session.Id, out var track)) _tracks[session.Id] = track = new Track();
                Read(session, track, now, items);
            }
            foreach (var gone in _tracks.Keys.Except(all.Select(s => s.Id)).ToList()) _tracks.Remove(gone);

            var ordered = Order(items);
            changed = !ordered.SequenceEqual(_items);
            if (changed) _items = ordered;
        }
        if (changed) Changed?.Invoke();
    }

    // Caller holds _gate; takes the session's own gate to read it.
    private void Read(LiveSession session, Track track, DateTimeOffset now, List<AttentionItem> items)
    {
        Need? need;
        string taskId, cwd;
        string? role;
        lock (session.Gate)
        {
            taskId = session.TaskId;
            cwd = session.Cwd;
            role = session.Role;
            need = session.Observed ? ObservedNeed(session) : ControlledNeed(session, track, now);
        }

        if (need?.Key != track.DismissedNeed) track.DismissedNeed = null;
        else need = null;

        if (need is not null)
        {
            if (track.Need?.Key == need.Key) need = track.Need;
            else need = need with { Id = NewId(session.Id), Since = now };
        }
        track.Need = need;

        if (need is not null)
        {
            items.Add(new AttentionItem(need.Id, session.Id, taskId, need.Kind, need.Title, need.Detail, need.Since, need.RequestId, role, cwd));
        }
        else if (track.Finish is { Cleared: false } finish)
        {
            items.Add(new AttentionItem(finish.Id, session.Id, taskId, finish.Failed ? AttentionKind.Failed : AttentionKind.Finished,
                finish.Failed ? "stopped with an error" : "finished", finish.Detail, finish.Since, null, role, cwd));
        }
    }

    private static Need? ObservedNeed(LiveSession session) =>
        session.ObservedStatus == "waiting"
            ? new Need("waiting", AttentionKind.Waiting, "is waiting outside Ochre", Path.GetFileName(session.Cwd), null)
            : null;

    // Caller holds the session's gate. Also notes a turn ending or the process exiting, which is what raises a
    // finished item.
    private Need? ControlledNeed(LiveSession session, Track track, DateTimeOffset now)
    {
        var c = session.Conversation;

        if (c.LastTurn is { } turn && !ReferenceEquals(turn, track.KnownTurn))
        {
            track.KnownTurn = turn;
            track.Finish = new Finish(NewId(session.Id), now, turn.IsError,
                turn.IsError ? $"the turn failed ({turn.Subtype})" : Clip(LastLine(c, turn)));
        }
        if (session.LastExit is { } exit && !ReferenceEquals(exit, track.KnownExit))
        {
            track.KnownExit = exit;
            if (exit.Code != 0)
                track.Finish = new Finish(NewId(session.Id), now, true, Clip($"claude exited ({exit.Code}) {exit.Tail}".TrimEnd()));
        }
        // A new turn under way supersedes the last one's result, read or not.
        if (c.TurnActive && track.Finish is { } running) running.Cleared = true;

        if (c.PendingPermission is { Resolution: PermissionResolution.Pending } p)
        {
            var key = "p:" + p.Request.RequestId;
            if (p.IsQuestion)
            {
                var first = AskUserQuestionInput.Parse(p.Request.InputJson).FirstOrDefault();
                return new Need(key, AttentionKind.Question, "asks you a question", Clip(first?.Question ?? ""), p.Request.RequestId);
            }
            if (p.IsPlan)
                return new Need(key, AttentionKind.Plan, "has a plan for you to approve", Clip(FirstLine(PlanApprovalInput.Parse(p.Request.InputJson) ?? "")), p.Request.RequestId);
            return new Need(key, AttentionKind.Permission, $"wants to use {p.Request.ToolName}", Clip(Describe(p.Request)), p.Request.RequestId);
        }
        if (!session.Dormant && c.WaitingElsewhere)
            return new Need("elsewhere", AttentionKind.Elsewhere, "is waiting on a prompt Ochre can't show", "", null);
        return null;
    }

    private static string Describe(PermissionRequestEvent request)
    {
        JsonNode? input;
        try
        {
            input = JsonNode.Parse(request.InputJson);
        }
        catch (System.Text.Json.JsonException)
        {
            input = null;
        }
        try
        {
            return ToolSummary.Describe(request.ToolName, input);
        }
        catch (InvalidOperationException)
        {
            return request.Description;   // an input field of an unexpected type
        }
    }

    // The last line of prose the turn ended with: what an agent usually says it did.
    private static string LastLine(SessionConversation c, TurnResultEvent turn)
    {
        for (int i = c.Items.Count - 1; i >= 0; i--)
        {
            if (c.Items[i] is not AssistantMessageItem a || !ReferenceEquals(a.Result, turn)) continue;
            if (a.Parts.OfType<TextPart>().LastOrDefault(t => t.Length > 0) is { } text)
                return text.Text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        }
        return "";
    }

    private static string FirstLine(string text) => text.Split('\n').Select(l => l.Trim().TrimStart('#').Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    private static string Clip(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxDetail ? line : line[..(MaxDetail - 1)].TrimEnd() + "…";
    }

    private string NewId(string sessionId) => $"{sessionId}/{++_nextId}";

    private static List<AttentionItem> Order(List<AttentionItem> items) =>
    [
        .. items
            .GroupBy(i => i.TaskId, StringComparer.Ordinal)
            .OrderBy(g => g.Any(i => i.NeedsYou) ? 0 : 1)
            .ThenBy(g => g.Where(i => i.NeedsYou).Select(i => i.Since).DefaultIfEmpty(g.Min(i => i.Since)).Min())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .SelectMany(g => g.OrderBy(i => i.NeedsYou ? 0 : 1).ThenBy(i => i.Since).ThenBy(i => i.Id, StringComparer.Ordinal)),
    ];

    public void Dispose()
    {
        _sessions.Changed -= OnSessionChanged;
        _sessions.ListChanged -= Refresh;
    }

    // What the board remembers about one session between reads.
    private sealed class Track
    {
        public object? KnownTurn { get; set; }
        public object? KnownExit { get; set; }
        public Finish? Finish { get; set; }
        public Need? Need { get; set; }

        /// <summary>The need the user dismissed; it stays hidden until the session needs something else.</summary>
        public string? DismissedNeed { get; set; }
    }

    private sealed class Finish(string id, DateTimeOffset since, bool failed, string detail)
    {
        public string Id { get; } = id;
        public DateTimeOffset Since { get; } = since;
        public bool Failed { get; } = failed;
        public string Detail { get; } = detail;
        public bool Cleared { get; set; }
    }

    // Key tells one need from the next (a different prompt is a different need), so an item keeps its id and age
    // while it lasts.
    private sealed record Need(string Key, AttentionKind Kind, string Title, string Detail, string? RequestId)
    {
        public string Id { get; init; } = "";
        public DateTimeOffset Since { get; init; }
    }
}
