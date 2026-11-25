using System.Text.Json.Nodes;
using Ochre.Core;
using Ochre.Core.Claude;
using Ochre.Core.Claude.Control;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;

namespace Ochre.Host.Sessions;

/// <summary>A controlled session the host is running (or ran, until the host quits).</summary>
internal sealed class LiveSession
{
    public LiveSession(string id, string taskId, string cwd, string? role, ClaudeSessionController controller)
    {
        Id = id;
        TaskId = taskId;
        Cwd = cwd;
        Role = role;
        Controller = controller;
        Conversation.SetSessionId(id);
        Conversation.Changed += (item, _) => Dirty.Add(item);
        Conversation.Reset += () => ResetPending = true;
        Conversation.StateChanged += () => HeaderDirty = true;
    }

    /// <summary>The id it was launched with; the key the task and the UI know it by, even after a <c>/clear</c>.</summary>
    public string Id { get; }
    public string TaskId { get; }
    public string Cwd { get; }
    public string? Role { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public ClaudeSessionController Controller { get; }

    /// <summary>Not thread-safe: touch it, and everything below, only while holding <see cref="Gate"/>.</summary>
    public SessionConversation Conversation { get; } = new();
    public Lock Gate { get; } = new();
    public bool Ended { get; set; }

    // What changed since the last broadcast, so streaming sends only the items that moved.
    public HashSet<ConversationItem> Dirty { get; } = new(ReferenceEqualityComparer.Instance);
    public bool ResetPending { get; set; }
    public bool HeaderDirty { get; set; }

    public bool HasChanges => Dirty.Count > 0 || ResetPending || HeaderDirty;
}

/// <summary>Why <see cref="SessionManager.StartAsync"/> didn't launch.</summary>
internal enum StartRefusal { None, NeedsTrust }

/// <summary>
/// Launches agents from tasks and keeps them running while windows come and go (ADR 0008). Output is folded into
/// each session's <see cref="SessionConversation"/> and broadcast to every window in coalesced batches, so a
/// streaming reply costs a handful of messages a second rather than one per token.
/// </summary>
internal sealed class SessionManager : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(80);

    private readonly TaskStore _tasks;
    private readonly AppLog _log;
    private readonly OchreProfile _profile;
    private readonly Dictionary<string, LiveSession> _sessions = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly Timer _flush;

    public SessionManager(TaskStore tasks, AppLog log, OchreProfile profile)
    {
        _tasks = tasks;
        _log = log;
        _profile = profile;
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

    /// <summary>
    /// Starts an agent on <paramref name="taskId"/> in <paramref name="cwd"/> and records it on the task. A folder
    /// Claude Code hasn't been told to trust is refused unless <paramref name="trustFolder"/> says the user just
    /// agreed, because print mode skips Claude's own trust prompt.
    /// </summary>
    public async Task<(LiveSession? Session, StartRefusal Refusal)> StartAsync(
        string taskId, string cwd, string? role, string? permissionMode, string? firstPrompt, bool trustFolder)
    {
        if (_tasks.Get(taskId) is null) throw new KeyNotFoundException($"no task '{taskId}'");
        if (!DirectoryTrust.Evaluate(null, cwd))
        {
            if (!trustFolder) return (null, StartRefusal.NeedsTrust);
            if (!DirectoryTrust.Grant(null, cwd)) _log.Warn("could not record folder trust in .claude.json; launching anyway");
        }

        var id = Guid.NewGuid().ToString();
        var cli = AgentCli.Locate();
        var env = new Dictionary<string, string?>
        {
            ["OCHRE_TASK"] = taskId,
            ["OCHRE_SESSION"] = id,
            // The agent's CLI must reach this host, not another profile's.
            ["OCHRE_PROFILE"] = _profile.IsDefault ? null : _profile.Name,
        };
        string? note = null;
        if (cli is not null)
        {
            env["PATH"] = Path.GetDirectoryName(cli) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            note = AgentPrompt.For(taskId, Path.GetFileNameWithoutExtension(cli));
        }
        else
        {
            _log.Warn("the ochre CLI was not found; the agent won't be able to update its task");
        }

        var controller = new ClaudeSessionController { Log = _log.Info };
        var session = new LiveSession(id, taskId, cwd, role, controller);
        controller.EventReceived += ev => OnEvent(session, ev);
        controller.Exited += (code, tail) => OnExited(session, code, tail);
        lock (_gate) _sessions[id] = session;

        try
        {
            controller.Start(new SessionStartOptions(cwd)
            {
                NewSessionId = id,
                PermissionMode = permissionMode,
                AppendSystemPrompt = note,
                Environment = env,
            });
        }
        catch
        {
            lock (_gate) _sessions.Remove(id);
            throw;
        }

        await _tasks.UpdateAsync(taskId, "user", doc =>
        {
            doc.AddSession(new TaskSessionRef(id, cwd, null, "launched", role));
            return $"started session {Short(id)}" + (role is null ? "" : $" ({role})");
        }).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(firstPrompt)) Send(session, firstPrompt);
        return (session, StartRefusal.None);
    }

    public void Send(LiveSession session, string text)
    {
        lock (session.Gate)
        {
            if (session.Ended) throw new InvalidOperationException("the session has ended");
            session.Controller.SendPrompt(text);
            session.Conversation.AddUserPrompt(text);
        }
    }

    public void Interrupt(LiveSession session)
    {
        lock (session.Gate)
        {
            if (session.Ended) return;
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

    private void OnEvent(LiveSession session, SessionEvent ev)
    {
        lock (session.Gate)
        {
            session.Conversation.Apply(ev);
            ControlledSessions.SetActivity(session.Controller.SessionId ?? session.Id, ActivityOf(session.Conversation));
        }
    }

    private void OnExited(LiveSession session, int code, string tail)
    {
        lock (session.Gate)
        {
            session.Ended = true;
            session.Conversation.SessionEnded(code, tail);
            session.HeaderDirty = true;
        }
        _log.Info($"session {Short(session.Id)} exited with code {code}");
    }

    private static ControlledActivity ActivityOf(SessionConversation c) =>
        c.PendingPermission is not null || c.WaitingElsewhere ? ControlledActivity.Waiting
        : c.TurnActive ? ControlledActivity.Busy
        : ControlledActivity.Idle;

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

/// <summary>Finds the console CLI agents use to reach their task.</summary>
internal static class AgentCli
{
    /// <summary><c>OCHRE_CLI_EXE</c>, else <c>ochre.com</c> or <c>ochre-cli.exe</c> beside the host; in a dev build,
    /// also this checkout's CLI build. Null when none exists.</summary>
    public static string? Locate()
    {
        if (Environment.GetEnvironmentVariable("OCHRE_CLI_EXE") is { Length: > 0 } fromEnv && Path.IsPathFullyQualified(fromEnv) && File.Exists(fromEnv))
            return fromEnv;
        foreach (var name in new[] { "ochre.com", "ochre-cli.exe" })
        {
            var beside = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(beside)) return beside;
        }
#if DEBUG
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Ochre.slnx"))) continue;
            var dev = Path.Combine(dir.FullName, "src", "Ochre.Cli", "bin", "Debug", "net10.0", "ochre-cli.exe");
            return File.Exists(dev) ? dev : null;
        }
#endif
        return null;
    }
}
