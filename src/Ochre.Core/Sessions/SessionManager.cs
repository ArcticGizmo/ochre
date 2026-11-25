using System.Text.Json.Nodes;
using Ochre.Core.Claude;
using Ochre.Core.Claude.Control;
using Ochre.Core.Tasks;

namespace Ochre.Core.Sessions;

/// <summary>A session launched from a task: running, or dormant until something is sent to it.</summary>
internal sealed class LiveSession
{
    public LiveSession(string id, string taskId, string cwd, string? role, string? permissionMode, string claudeId, DateTimeOffset startedAt,
        ClaudeSessionController controller)
    {
        Id = id;
        TaskId = taskId;
        Cwd = cwd;
        Role = role;
        PermissionMode = permissionMode;
        ClaudeId = claudeId;
        StartedAt = startedAt;
        Controller = controller;
        Conversation.SetSessionId(claudeId);
        Conversation.Changed += (item, _) => Dirty.Add(item);
        Conversation.Reset += () => ResetPending = true;
        Conversation.StateChanged += () => HeaderDirty = true;
    }

    /// <summary>The id it was launched with; the key the task and the UI know it by, even after a <c>/clear</c>.</summary>
    public string Id { get; }
    public string TaskId { get; }
    public string Cwd { get; }
    public string? Role { get; }
    public string? PermissionMode { get; }
    public DateTimeOffset StartedAt { get; }
    public ClaudeSessionController Controller { get; }

    /// <summary>Not thread-safe: touch it, and everything below, only while holding <see cref="Gate"/>.</summary>
    public SessionConversation Conversation { get; } = new();
    public Lock Gate { get; } = new();

    /// <summary>The Claude session a resume continues.</summary>
    public string ClaudeId { get; set; }

    /// <summary>No process: it ended, or the host started after it ran. Sending resumes it.</summary>
    public bool Dormant { get; set; }

    /// <summary>The thread holds what the transcript had (or the session ran in this host, so it never needed it).</summary>
    public bool HistoryLoaded { get; set; }

    // What changed since the last broadcast, so streaming sends only the items that moved.
    public HashSet<ConversationItem> Dirty { get; } = new(ReferenceEqualityComparer.Instance);
    public bool ResetPending { get; set; }
    public bool HeaderDirty { get; set; }

    public bool HasChanges => Dirty.Count > 0 || ResetPending || HeaderDirty;
}

/// <summary>Why <see cref="SessionManager.StartAsync"/> didn't launch.</summary>
internal enum StartRefusal { None, NeedsTrust }

/// <summary>
/// Launches agents from tasks and keeps them while windows come and go (ADR 0008). Output is folded into each
/// session's <see cref="SessionConversation"/> and broadcast to every window in coalesced batches, so a streaming
/// reply costs a handful of messages a second rather than one per token. Sessions from earlier runs come back
/// dormant: their thread loads from the transcript, and sending to one resumes it under the same id.
/// </summary>
internal sealed class SessionManager : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(80);

    // Past this many transcript lines the thread shows only the tail; the CLI keeps the full context regardless.
    private const int MaxHistoryLines = 4000;

    private readonly TaskStore _tasks;
    private readonly SessionLedger _ledger;
    private readonly AppLog _log;
    private readonly OchreProfile _profile;
    private readonly SessionSystem _system;
    private readonly Dictionary<string, LiveSession> _sessions = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly Timer _flush;

    public SessionManager(TaskStore tasks, SessionLedger ledger, AppLog log, OchreProfile profile, SessionSystem? system = null)
    {
        _tasks = tasks;
        _ledger = ledger;
        _log = log;
        _profile = profile;
        _system = system ?? new SessionSystem();
        _flush = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
    }

    /// <summary>A session changed: raised from the flush timer with that session's pending changes taken.</summary>
    public event Action<LiveSession, SessionChanges>? Changed;

    public IReadOnlyList<LiveSession> All
    {
        get
        {
            lock (_gate) return [.. _sessions.Values.OrderBy(s => s.StartedAt)];
        }
    }

    public LiveSession? Get(string id)
    {
        lock (_gate) return _sessions.GetValueOrDefault(id);
    }

    /// <summary>The name agents call the CLI by (<c>ochre</c>, or <c>ochre-cli</c> in a dev checkout).</summary>
    public string AgentCommand => _system.LocateCli() is { } cli ? Path.GetFileNameWithoutExtension(cli) : "ochre";

    /// <summary>Brings back, dormant, every session a task says it launched. Cheap: no transcript is read until a
    /// thread is opened.</summary>
    public void LoadDormant()
    {
        foreach (var summary in _tasks.List())
        {
            if (_tasks.Get(summary.Id) is not { } doc) continue;
            foreach (var s in doc.Sessions.Where(s => s.Binding == "launched" && ClaudeCli.IsSessionId(s.Id)))
            {
                var entry = _ledger.Get(s.Id);
                var session = Create(s.Id, summary.Id, s.Cwd, s.Role, entry?.PermissionMode, entry?.ClaudeId ?? s.Id, entry?.StartedAt ?? DateTimeOffset.MinValue);
                session.Dormant = true;
                lock (_gate) _sessions.TryAdd(s.Id, session);
            }
        }
    }

    /// <summary>
    /// Starts an agent on <paramref name="taskId"/> in <paramref name="cwd"/> and records it on the task. A folder
    /// Claude Code hasn't been told to trust is refused unless <paramref name="trustFolder"/> says the user just
    /// agreed, because print mode skips Claude's own trust prompt.
    /// </summary>
    public async Task<(LiveSession? Session, StartRefusal Refusal)> StartAsync(
        string taskId, string cwd, string? role, string? permissionMode, string? firstPrompt, bool trustFolder)
    {
        if (_tasks.Get(taskId) is null) throw new KeyNotFoundException($"no task '{taskId}'");
        if (!_system.IsTrusted(cwd))
        {
            if (!trustFolder) return (null, StartRefusal.NeedsTrust);
            if (!_system.GrantTrust(cwd)) _log.Warn("could not record folder trust in .claude.json; launching anyway");
        }

        var id = Guid.NewGuid().ToString();
        var session = Create(id, taskId, cwd, role, permissionMode, id, DateTimeOffset.UtcNow);
        session.HistoryLoaded = true;
        lock (_gate) _sessions[id] = session;

        try
        {
            session.Controller.Start(Options(session) with { NewSessionId = id });
        }
        catch
        {
            lock (_gate) _sessions.Remove(id);
            throw;
        }
        _ledger.Upsert(new LedgerEntry(id, taskId, cwd, null, role, permissionMode, id, session.StartedAt));

        await _tasks.UpdateAsync(taskId, "user", doc =>
        {
            doc.AddSession(new TaskSessionRef(id, cwd, null, "launched", role));
            return $"started session {Short(id)}" + (role is null ? "" : $" ({role})");
        }).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(firstPrompt)) Send(session, firstPrompt);
        return (session, StartRefusal.None);
    }

    /// <summary>Fills a dormant session's thread from its transcript, once. The read and parse run off the caller's
    /// thread; the result lands in front of anything already in the thread.</summary>
    public async Task EnsureHistoryAsync(LiveSession session)
    {
        lock (session.Gate)
        {
            if (session.HistoryLoaded) return;
            session.HistoryLoaded = true;
        }
        var claudeId = session.ClaudeId;
        var (history, clipped, found) = await Task.Run(() =>
        {
            var path = _system.ResolveTranscript(claudeId, session.Cwd);
            if (path is null) return ((IReadOnlyList<SessionConversation.ParsedTranscriptLine>)[], false, false);
            var (lines, clip) = TranscriptScan.LastLines(TranscriptChain.ActiveLines(() => TranscriptScan.ReadLines(path)), MaxHistoryLines);
            return (SessionConversation.ParseHistory(lines, claudeId), clip, true);
        }).ConfigureAwait(false);

        lock (session.Gate)
        {
            if (!found)
            {
                session.Conversation.AddNote("no conversation recorded yet; sending starts it");
                return;
            }
            int n = session.Conversation.LoadHistory(history);
            if (clipped && n > 0) session.Conversation.AddNote("showing the most recent part of a long transcript");
            session.ResetPending = true;
        }
    }

    public void Send(LiveSession session, string text)
    {
        lock (session.Gate)
        {
            if (session.Dormant) Resume(session);
            session.Controller.SendPrompt(text);
            session.Conversation.AddUserPrompt(text);
        }
    }

    // Caller holds the session's gate. Continues the same Claude session, so the task binding and OCHRE_SESSION hold.
    private void Resume(LiveSession session)
    {
        if (_system.FindLive(session.ClaudeId) is { } elsewhere)
            throw new InvalidOperationException($"this session is open in another Claude Code (process {elsewhere.Pid}); close it there first");
        if (!_system.FolderExists(session.Cwd)) throw new InvalidOperationException($"its folder no longer exists: {session.Cwd}");
        if (!_system.IsTrusted(session.Cwd))
            throw new InvalidOperationException("Claude Code no longer trusts its folder; start a new agent to be asked again");

        // A session that never got a message wrote no transcript, and --resume would fail on it; starting it under the
        // same id keeps the binding.
        var hasTranscript = _system.ResolveTranscript(session.ClaudeId, session.Cwd) is not null;
        session.Controller.Start(hasTranscript
            ? Options(session) with { ResumeSessionId = session.ClaudeId }
            : Options(session) with { NewSessionId = session.ClaudeId });
        session.Dormant = false;
        session.HeaderDirty = true;
        session.Conversation.AddNote(hasTranscript ? "resumed" : "started");
        _log.Info($"session {Short(session.Id)} resumed");
    }

    public void Interrupt(LiveSession session)
    {
        lock (session.Gate)
        {
            if (session.Dormant) return;
            session.Conversation.NoteInterrupt();
            session.Controller.Interrupt();
        }
    }

    public void Stop(LiveSession session) => session.Controller.Stop();

    /// <summary>Answers the pending permission, question or plan approval with id <paramref name="requestId"/>.
    /// False when that prompt is no longer pending (answered elsewhere, or the turn moved on).</summary>
    public bool Respond(LiveSession session, string requestId, bool allow, string? switchMode,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? answers)
    {
        lock (session.Gate)
        {
            if (session.Conversation.PendingPermission is not { } item || item.Request.RequestId != requestId) return false;
            string? summary = null;
            JsonNode? updated = null;
            if (allow && item.IsQuestion && answers is not null)
            {
                updated = AskUserQuestionInput.BuildAnswer(item.Request.InputJson, answers);
                summary = AskUserQuestionInput.Summarise(AskUserQuestionInput.Parse(item.Request.InputJson), answers);
            }
            session.Controller.RespondToPermission(item.Request, allow, updatedInput: updated);
            if (allow && switchMode is not null) session.Controller.SetPermissionMode(switchMode);
            session.Conversation.ResolvePermission(item, allow, allow ? switchMode : null, summary);
            return true;
        }
    }

    private LiveSession Create(string id, string taskId, string cwd, string? role, string? permissionMode, string claudeId, DateTimeOffset startedAt)
    {
        var controller = new ClaudeSessionController(_system.Launcher) { Log = _log.Info };
        var session = new LiveSession(id, taskId, cwd, role, permissionMode, claudeId, startedAt, controller);
        controller.EventReceived += ev => OnEvent(session, ev);
        controller.Exited += (code, tail) => OnExited(session, code, tail);
        return session;
    }

    // What every launch and resume of an agent carries: its task binding, the CLI on PATH, and what it's told.
    private SessionStartOptions Options(LiveSession session)
    {
        var env = new Dictionary<string, string?>
        {
            ["OCHRE_TASK"] = session.TaskId,
            ["OCHRE_SESSION"] = session.Id,
            // The agent's CLI must reach this host, not another profile's.
            ["OCHRE_PROFILE"] = _profile.IsDefault ? null : _profile.Name,
        };
        string? note = null;
        IReadOnlyList<string> allowed = [];
        if (_system.LocateCli() is { } cli)
        {
            var command = Path.GetFileNameWithoutExtension(cli);
            env["PATH"] = Path.GetDirectoryName(cli) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            note = AgentPrompt.For(session.TaskId, command);
            allowed = AgentPrompt.AllowedTools(command);
        }
        else
        {
            _log.Warn("the ochre CLI was not found; the agent won't be able to update its task");
        }
        return new SessionStartOptions(session.Cwd)
        {
            PermissionMode = session.PermissionMode,
            AppendSystemPrompt = note,
            AllowedTools = allowed,
            Environment = env,
        };
    }

    private void OnEvent(LiveSession session, SessionEvent ev)
    {
        lock (session.Gate)
        {
            session.Conversation.Apply(ev);
            if (ev is SessionInitEvent && session.Controller.SessionId is { } now && now != session.ClaudeId)
            {
                // `/clear` started a new Claude session; a later resume must continue that one.
                session.ClaudeId = now;
                _ledger.Rebind(session.Id, now);
            }
            ControlledSessions.SetActivity(session.Controller.SessionId ?? session.Id, ActivityOf(session.Conversation));
        }
    }

    private void OnExited(LiveSession session, int code, string tail)
    {
        lock (session.Gate)
        {
            session.Dormant = true;
            session.Conversation.SessionEnded(code, tail);
            session.HeaderDirty = true;
        }
        _log.Info($"session {Short(session.Id)} exited with code {code}");
    }

    private static ControlledActivity ActivityOf(SessionConversation c) =>
        c.PendingPermission is not null || c.WaitingElsewhere ? ControlledActivity.Waiting
        : c.TurnActive ? ControlledActivity.Busy
        : ControlledActivity.Idle;

    /// <summary>Broadcasts pending changes now rather than on the next timer tick; for tests.</summary>
    internal void FlushNow() => Flush();

    private void Flush()
    {
        foreach (var session in All)
        {
            SessionChanges? changes = null;
            lock (session.Gate)
            {
                if (!session.HasChanges) continue;
                changes = SessionChanges.Take(session);
            }
            try
            {
                Changed?.Invoke(session, changes);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                _log.Error("session broadcast failed", ex);
            }
        }
    }

    private static string Short(string id) => id.Length > 8 ? id[..8] : id;

    public void Dispose()
    {
        _flush.Dispose();
        foreach (var session in All) session.Controller.Stop();
    }
}

/// <summary>A session's pending changes, taken (and cleared) under its gate. Items are copied out as indices so the
/// broadcast can be built later; the caller re-reads them under the gate.</summary>
internal sealed record SessionChanges(bool Reset, bool Header, IReadOnlyList<int> Items)
{
    public static SessionChanges Take(LiveSession s)
    {
        var items = s.ResetPending
            ? []
            : s.Dirty.Select(i => IndexOf(s.Conversation.Items, i)).Where(i => i >= 0).Order().ToList();
        var changes = new SessionChanges(s.ResetPending, s.HeaderDirty || s.ResetPending, items);
        s.Dirty.Clear();
        s.ResetPending = false;
        s.HeaderDirty = false;
        return changes;
    }

    private static int IndexOf(IReadOnlyList<ConversationItem> items, ConversationItem item)
    {
        // Changes cluster at the end of the thread, so search from there.
        for (int i = items.Count - 1; i >= 0; i--)
            if (ReferenceEquals(items[i], item)) return i;
        return -1;
    }
}

/// <summary>
/// What <see cref="SessionManager"/> reads from outside the task store: how <c>claude</c> starts, where the agent
/// CLI is, Claude Code's folder trust, its live-session files and its transcripts. The defaults are the real ones;
/// tests replace them so a session's whole lifecycle runs in memory and never touches the user's <c>~/.claude</c>.
/// </summary>
internal sealed record SessionSystem
{
    public IClaudeLauncher Launcher { get; init; } = SystemClaudeLauncher.Instance;
    public Func<string?> LocateCli { get; init; } = () => null;
    public Func<string, bool> IsTrusted { get; init; } = cwd => DirectoryTrust.Evaluate(null, cwd);
    public Func<string, bool> GrantTrust { get; init; } = cwd => DirectoryTrust.Grant(null, cwd);
    public Func<string, LiveClaudeSession?> FindLive { get; init; } = id => LiveClaudeSessions.Find(id);
    public Func<string, string, string?> ResolveTranscript { get; init; } = (id, cwd) => TranscriptLocator.Resolve(id, cwd);
    public Func<string, bool> FolderExists { get; init; } = Directory.Exists;
}
