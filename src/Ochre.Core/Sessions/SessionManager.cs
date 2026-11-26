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

    /// <summary>The task it belongs to; empty for an observed session nobody has linked yet.</summary>
    public string TaskId { get; set; }
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

    /// <summary>The model the user picked; null leaves Claude's default.</summary>
    public string? Model { get; set; }

    /// <summary>The effort level the user picked; null is "auto".</summary>
    public string? Effort { get; set; }

    /// <summary>Started outside Ochre (in a terminal, the desktop app, another tool). Ochre only reads its transcript;
    /// it never sends to it, stops it or lets it write to a task.</summary>
    public bool Observed { get; init; }

    /// <summary>For an observed session: busy, idle or waiting from Claude's session file, or closed once its
    /// process has gone.</summary>
    public string ObservedStatus { get; set; } = "closed";

    /// <summary>The git branch its transcript last recorded, for binding suggestions.</summary>
    public string? Branch { get; set; }

    // Observed sessions only. Reads happen under TailGate (the scan and the first thread load race for them); what
    // they return is applied to the conversation under Gate.
    internal Lock TailGate { get; } = new();
    internal string? TranscriptPath { get; set; }
    internal TranscriptTailReader? ThreadTail { get; set; }
    internal TranscriptTailReader? BranchTail { get; set; }

    /// <summary>No process: it ended, or the host started after it ran. Sending resumes it.</summary>
    public bool Dormant { get; set; }

    /// <summary>How its process last ended in this host run; null while it has never exited here.</summary>
    public SessionExit? LastExit { get; set; }

    /// <summary>The thread holds what the transcript had (or the session ran in this host, so it never needed it).</summary>
    public bool HistoryLoaded { get; set; }

    // What changed since the last broadcast, so streaming sends only the items that moved.
    public HashSet<ConversationItem> Dirty { get; } = new(ReferenceEqualityComparer.Instance);
    public bool ResetPending { get; set; }
    public bool HeaderDirty { get; set; }

    public bool HasChanges => Dirty.Count > 0 || ResetPending || HeaderDirty;
}

/// <summary>A process exit. A class, so each exit is a distinct instance a watcher can tell from the one before.</summary>
internal sealed class SessionExit(int code, string tail)
{
    public int Code { get; } = code;
    public string Tail { get; } = tail;
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

    // Enough recent records to find the branch an observed session is on without reading its whole transcript.
    private const int BranchScanLines = 50;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);

    private const string ReadOnly = "this session was started outside Ochre, so it's read-only here";

    private readonly TaskStore _tasks;
    private readonly SessionLedger _ledger;
    private readonly AppLog _log;
    private readonly OchreProfile _profile;
    private readonly SessionSystem _system;
    private readonly Dictionary<string, LiveSession> _sessions = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly Timer _flush;
    private Timer? _scan;
    private int _scanning;

    public SessionManager(TaskStore tasks, SessionLedger ledger, AppLog log, OchreProfile profile, SessionSystem? system = null)
    {
        _tasks = tasks;
        _ledger = ledger;
        _log = log;
        _profile = profile;
        _system = system ?? new SessionSystem();
        _flush = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
    }

    /// <summary>Starts looking for sessions running outside Ochre every couple of seconds.</summary>
    public void StartObserving() => _scan ??= new Timer(_ => ScanObserved(), null, TimeSpan.Zero, ScanInterval);

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

    /// <summary>Brings back, dormant, every session a task says it launched, and every outside session linked to a
    /// task (closed until the scan finds its process). Cheap: no transcript is read until a thread is opened.</summary>
    public void LoadDormant()
    {
        foreach (var summary in _tasks.List())
        {
            if (_tasks.Get(summary.Id) is not { } doc) continue;
            foreach (var s in doc.Sessions.Where(s => ClaudeCli.IsSessionId(s.Id)))
            {
                LiveSession session;
                if (s.Binding == "launched")
                {
                    var entry = _ledger.Get(s.Id);
                    session = Create(s.Id, summary.Id, s.Cwd, s.Role, entry?.PermissionMode, entry?.ClaudeId ?? s.Id, entry?.StartedAt ?? DateTimeOffset.MinValue);
                    session.Model = entry?.Model;
                    session.Effort = entry?.Effort;
                }
                else
                {
                    session = Create(s.Id, summary.Id, s.Cwd, s.Role, null, s.Id, DateTimeOffset.MinValue, observed: true);
                }
                session.Dormant = true;
                lock (_gate) _sessions.TryAdd(s.Id, session);
            }
        }
    }

    /// <summary>The live session list gained or lost a session, or an observed one's branch changed (so its
    /// suggestions may have). Raised from the scan.</summary>
    public event Action? ListChanged;

    /// <summary>
    /// Picks up Claude sessions running outside Ochre from Claude's own session files (ADR 0006), refreshes their
    /// status and branch, follows the transcript of any whose thread is open, and drops unlinked ones whose process
    /// has gone. A linked one stays, closed, so its thread can still be read from the task.
    /// </summary>
    public void ScanObserved()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            var live = _system.ReadLive();
            var all = All;
            var controlled = all.Where(s => !s.Observed).Select(s => s.ClaudeId).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool listChanged = false;

            foreach (var l in live)
            {
                if (controlled.Contains(l.SessionId) || !ClaudeCli.IsSessionId(l.SessionId)) continue;
                seen.Add(l.SessionId);
                var session = Get(l.SessionId);
                if (session is null)
                {
                    session = Create(l.SessionId, "", l.Cwd, null, null, l.SessionId, l.StartedAt is { } t ? new DateTimeOffset(t) : DateTimeOffset.UtcNow, observed: true);
                    lock (_gate) _sessions.TryAdd(l.SessionId, session);
                    listChanged = true;
                }
                else if (!session.Observed)
                {
                    continue;
                }
                var status = l.Status is "busy" or "waiting" ? l.Status : "idle";
                lock (session.Gate)
                {
                    if (session.ObservedStatus != status || session.Dormant)
                    {
                        session.ObservedStatus = status;
                        session.Dormant = false;
                        session.HeaderDirty = true;
                        if (session.ThreadTail is not null) session.Conversation.SetLiveTail(status == "busy");
                    }
                }
                if (FollowTranscript(session)) listChanged = true;
            }

            foreach (var session in all.Where(s => s.Observed && !seen.Contains(s.Id)))
            {
                if (session.TaskId.Length == 0)
                {
                    lock (_gate) _sessions.Remove(session.Id);
                    listChanged = true;
                    continue;
                }
                lock (session.Gate)
                {
                    if (session.ObservedStatus == "closed") continue;
                    session.ObservedStatus = "closed";
                    session.Dormant = true;
                    if (session.ThreadTail is not null) session.Conversation.SetLiveTail(false);
                    session.HeaderDirty = true;
                }
            }

            if (listChanged) ListChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // A scan is retried in two seconds; one that fails must never take the host down.
            _log.Error("observed session scan failed", ex);
        }
        finally
        {
            Volatile.Write(ref _scanning, 0);
        }
    }

    // Reads what an observed session's transcript gained: the branch it last recorded, and, while its thread is
    // open, the new lines for the thread. True when the branch changed.
    private bool FollowTranscript(LiveSession session)
    {
        session.TranscriptPath ??= _system.ResolveTranscript(session.Id, session.Cwd);
        if (session.TranscriptPath is not { } path) return false;

        IReadOnlyList<string> branchLines;
        IReadOnlyList<string> threadLines = [];
        lock (session.TailGate)
        {
            session.BranchTail ??= new TranscriptTailReader(path, BranchScanLines);
            branchLines = session.BranchTail.Read().Lines;
            if (session.ThreadTail is { } thread) threadLines = thread.Read().Lines;
        }

        var branch = branchLines.Select(BranchOf).LastOrDefault(b => b is not null);
        lock (session.Gate)
        {
            foreach (var line in threadLines) session.Conversation.AppendTranscriptLine(line);
            if (branch is null || branch == session.Branch) return false;
            session.Branch = branch;
            session.HeaderDirty = true;
            return true;
        }
    }

    // Every transcript record carries the branch the session was on; reading it needs no git call.
    private static string? BranchOf(string line)
    {
        if (!line.Contains("\"gitBranch\"", StringComparison.Ordinal)) return null;
        try
        {
            return TranscriptJson.AsString(JsonNode.Parse(line)?["gitBranch"]) is { Length: > 0 } b && b != "HEAD" ? b : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Links an observed session to <paramref name="taskId"/> (<c>binding: confirmed</c>). It stays read-only.</summary>
    public async Task BindAsync(LiveSession session, string taskId)
    {
        if (!session.Observed) throw new InvalidOperationException("only a session started outside Ochre can be linked to a task");
        if (session.TaskId.Length > 0) throw new InvalidOperationException($"this session is already linked to {session.TaskId}");
        await _tasks.UpdateAsync(taskId, "user", doc =>
        {
            if (doc.Sessions.Any(s => s.Id == session.Id)) return null;
            doc.AddSession(new TaskSessionRef(session.Id, session.Cwd, null, "confirmed", null));
            return $"linked session {Short(session.Id)}";
        }).ConfigureAwait(false);
        lock (session.Gate)
        {
            session.TaskId = taskId;
            session.HeaderDirty = true;
        }
        ListChanged?.Invoke();
    }

    /// <summary>Takes a linked observed session off its task. Sessions the task launched can't be unlinked.</summary>
    public async Task UnbindAsync(LiveSession session)
    {
        if (!session.Observed) throw new InvalidOperationException("an agent the task started can't be unlinked from it");
        var taskId = session.TaskId;
        if (taskId.Length == 0) return;
        await _tasks.UpdateAsync(taskId, "user", doc => doc.RemoveSession(session.Id) ? $"unlinked session {Short(session.Id)}" : null).ConfigureAwait(false);
        lock (session.Gate)
        {
            session.TaskId = "";
            session.HeaderDirty = true;
        }
        if (session.ObservedStatus == "closed")
            lock (_gate) _sessions.Remove(session.Id);
        ListChanged?.Invoke();
    }

    /// <summary>Tasks an unlinked observed session looks like it belongs to, best first. Only suggestions: the user
    /// links it.</summary>
    public IReadOnlyList<BindSuggestion> Suggest(LiveSession session) =>
        session.Observed && session.TaskId.Length == 0 ? BindSuggestions.For(session.Cwd, session.Branch, _tasks.List()) : [];

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
        if (session.Observed)
        {
            await LoadObservedThreadAsync(session).ConfigureAwait(false);
            return;
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

    // An observed thread is the tail of its transcript: the last lines now, then whatever the scan reads next. The
    // history goes in front of anything the scan appended meanwhile, so the order holds.
    private async Task LoadObservedThreadAsync(LiveSession session)
    {
        var (history, clipped, found) = await Task.Run(() =>
        {
            session.TranscriptPath ??= _system.ResolveTranscript(session.Id, session.Cwd);
            if (session.TranscriptPath is not { } path) return ((IReadOnlyList<SessionConversation.ParsedTranscriptLine>)[], false, false);
            TailRead first;
            lock (session.TailGate)
            {
                session.ThreadTail = new TranscriptTailReader(path, MaxHistoryLines);
                first = session.ThreadTail.Read();
            }
            return (SessionConversation.ParseHistory(first.Lines, session.Id), first.SkippedEarlier, true);
        }).ConfigureAwait(false);

        lock (session.Gate)
        {
            if (!found)
            {
                session.Conversation.AddNote("no transcript found for this session yet");
                return;
            }
            int n = session.Conversation.LoadHistory(history);
            if (clipped && n > 0) session.Conversation.AddNote("showing the most recent part of a long transcript");
            session.Conversation.SetLiveTail(session.ObservedStatus == "busy");
            session.ResetPending = true;
        }
    }

    public void Send(LiveSession session, string text)
    {
        if (session.Observed) throw new InvalidOperationException(ReadOnly);
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
        if (session.Observed) throw new InvalidOperationException(ReadOnly);
        lock (session.Gate)
        {
            if (session.Dormant) return;
            session.Conversation.NoteInterrupt();
            session.Controller.Interrupt();
        }
    }

    public void Stop(LiveSession session)
    {
        if (session.Observed) throw new InvalidOperationException(ReadOnly);
        session.Controller.Stop();
    }

    /// <summary>Switches the model now if the agent is running, and for every later resume.</summary>
    public void SetModel(LiveSession session, string model)
    {
        if (session.Observed) throw new InvalidOperationException(ReadOnly);
        string? effort;
        lock (session.Gate)
        {
            if (session.Model == model) return;
            session.Model = model;
            effort = session.Effort;
            if (!session.Dormant) session.Controller.SetModel(model);
            session.Conversation.AddNote($"model → {model}");
            session.HeaderDirty = true;
        }
        _ledger.SetChoices(session.Id, model, effort);
    }

    /// <summary>Sets the effort now if the agent is running, and for every later resume. "auto" clears it.</summary>
    public void SetEffort(LiveSession session, string level)
    {
        if (session.Observed) throw new InvalidOperationException(ReadOnly);
        var effort = level == "auto" ? null : level;
        string? model;
        lock (session.Gate)
        {
            if (session.Effort == effort) return;
            session.Effort = effort;
            model = session.Model;
            if (!session.Dormant) session.Controller.SetEffort(level);
            session.Conversation.AddNote($"effort → {level}");
            session.HeaderDirty = true;
        }
        _ledger.SetChoices(session.Id, model, effort);
    }

    /// <summary>Answers the pending permission, question or plan approval with id <paramref name="requestId"/>.
    /// False when that prompt is no longer pending (answered elsewhere, or the turn moved on).</summary>
    public bool Respond(LiveSession session, string requestId, bool allow, string? switchMode,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? answers)
    {
        if (session.Observed) return false;
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

    private LiveSession Create(string id, string taskId, string cwd, string? role, string? permissionMode, string claudeId, DateTimeOffset startedAt,
        bool observed = false)
    {
        // An observed session gets a controller too, so every session has the same shape, but it is never started.
        var controller = new ClaudeSessionController(_system.Launcher) { Log = _log.Info };
        var session = new LiveSession(id, taskId, cwd, role, permissionMode, claudeId, startedAt, controller) { Observed = observed };
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
            Model = session.Model,
            Effort = session.Effort,
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
            session.LastExit = new SessionExit(code, tail);
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
        _scan?.Dispose();
        foreach (var session in All.Where(s => !s.Observed)) session.Controller.Stop();
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
    public Func<IReadOnlyList<LiveClaudeSession>> ReadLive { get; init; } = () => LiveClaudeSessions.Read();
    public Func<string, string, string?> ResolveTranscript { get; init; } = (id, cwd) => TranscriptLocator.Resolve(id, cwd);
    public Func<string, bool> FolderExists { get; init; } = Directory.Exists;
}
