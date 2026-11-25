using Ochre.Core.Tasks;

namespace Ochre.Core.Sessions;

/// <summary>
/// Parks a task (task-model.md, "Lifecycle"): the task goes Parked with the user's note, each running agent is asked
/// for its state and next step through <c>task park-note</c>, and then stopped, so it waits dormant with its
/// conversation until someone sends to it. A busy agent gets until <c>busyWait</c> to finish its turn before it is
/// interrupted; an agent that never writes a note is stopped after <c>noteWait</c> regardless.
/// </summary>
internal sealed class ParkCoordinator
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    private readonly TaskStore _tasks;
    private readonly SessionManager _sessions;
    private readonly ParkRequests _requests;
    private readonly AppLog _log;
    private readonly TimeSpan _busyWait;
    private readonly TimeSpan _noteWait;

    internal ParkCoordinator(TaskStore tasks, SessionManager sessions, ParkRequests requests, AppLog log, TimeSpan? busyWait = null, TimeSpan? noteWait = null)
    {
        _tasks = tasks;
        _sessions = sessions;
        _requests = requests;
        _log = log;
        _busyWait = busyWait ?? TimeSpan.FromMinutes(2);
        _noteWait = noteWait ?? TimeSpan.FromMinutes(2);
    }

    /// <summary>Raised when the set of agents a park is waiting on changes (from any thread).</summary>
    public event Action<string>? Changed;

    /// <summary>The ids of the agents a park of <paramref name="taskId"/> is still waiting on.</summary>
    public IReadOnlyList<string> WaitingFor(string taskId) => _requests.OpenFor(taskId);

    /// <summary>
    /// Sets the task Parked with <paramref name="userNote"/> straight away, then collects notes from its running
    /// agents and stops them. The returned task completes when every agent has gone dormant.
    /// </summary>
    public async Task<Task> ParkAsync(string taskId, string? userNote)
    {
        var note = userNote?.Trim() ?? "";
        var now = DateTimeOffset.UtcNow;
        await _tasks.UpdateAsync(taskId, "user", doc =>
        {
            doc.State = TaskState.Parked;
            doc.Park = new ParkNote(now, note);
            return "parked";
        }).ConfigureAwait(false);

        var running = _sessions.All.Where(s => s.TaskId == taskId && !s.Dormant).ToList();
        foreach (var s in running) _requests.Open(taskId, s.Id);
        if (running.Count > 0) Changed?.Invoke(taskId);
        return Task.WhenAll(running.Select(s => Task.Run(() => CollectAsync(s))));
    }

    private async Task CollectAsync(LiveSession s)
    {
        try
        {
            if (!await WaitUntil(() => Idle(s), _busyWait).ConfigureAwait(false))
            {
                _sessions.Interrupt(s);
                await WaitUntil(() => Idle(s), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            if (!s.Dormant)
            {
                _sessions.Send(s, AgentPrompt.ParkPrompt(_sessions.AgentCommand));
                // The note arrives mid-turn; let the turn finish so the agent's last words land in the transcript.
                if (await WaitUntil(() => !_requests.IsOpen(s.TaskId, s.Id), _noteWait).ConfigureAwait(false))
                    await WaitUntil(() => Idle(s), TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            _log.Warn($"park: couldn't ask session {s.Id[..8]} for a note: {ex.Message}");
        }
        finally
        {
            if (_requests.TryTake(s.TaskId, s.Id)) _log.Info($"park: session {s.Id[..8]} left no note");
            _sessions.Stop(s);
            Changed?.Invoke(s.TaskId);
        }
    }

    private static bool Idle(LiveSession s)
    {
        lock (s.Gate) return s.Dormant || (!s.Conversation.TurnActive && s.Conversation.PendingPermission is null);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(Poll).ConfigureAwait(false);
        }
        return true;
    }
}
