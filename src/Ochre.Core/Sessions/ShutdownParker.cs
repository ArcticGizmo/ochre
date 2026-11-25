using System.Text.RegularExpressions;
using Ochre.Core.Tasks;

namespace Ochre.Core.Sessions;

/// <summary>
/// Optional auto-park when Windows ends the session (implementation-plan M2 slice 5). There's no time to ask agents
/// for notes, so each task with running agents is parked straight away with an automatic note, and the agents are left
/// to end with the system; they come back dormant. The shutdown is stamped in the ledger with what each task had
/// before. If Ochre is still running a while later, the shutdown was cancelled, and the tasks are put back as they were.
/// </summary>
internal sealed partial class ShutdownParker(TaskStore tasks, SessionManager sessions, SessionLedger ledger, Func<bool> enabled, AppLog log)
{
    /// <summary>How long after the shutdown request Ochre must still be running before it counts as cancelled.</summary>
    public static readonly TimeSpan CancelledAfter = TimeSpan.FromSeconds(90);

    // Windows gives a process about five seconds to answer WM_QUERYENDSESSION; the task writes must fit well inside.
    private static readonly TimeSpan WriteBudget = TimeSpan.FromSeconds(3);

    private const string Author = "ochre";

    private Timer? _cancelCheck;

    /// <summary>Windows is ending the session. Returns once the tasks are parked, or the time budget runs out.</summary>
    public void OnSessionEnding()
    {
        if (!enabled()) return;
        var now = DateTimeOffset.UtcNow;
        var running = sessions.All.Where(s => !s.Observed && !s.Dormant && s.TaskId.Length > 0).GroupBy(s => s.TaskId).ToList();
        if (running.Count == 0) return;

        var parked = new List<ShutdownParkedTask>();
        var writes = running.Select(group => Task.Run(async () =>
        {
            var names = group.Select(s => s.Role ?? s.Id[..8]).ToList();
            ShutdownParkedTask? record = null;
            await tasks.UpdateAsync(group.Key, Author, doc =>
            {
                if (doc.State is TaskState.Parked or TaskState.Done) return null;
                var note = AutoNote(names, LastTicked(doc));
                record = new ShutdownParkedTask(group.Key, doc.State.ToString().ToLowerInvariant(), doc.Park?.At, doc.Park?.Note, note);
                doc.Park = new ParkNote(now, note);
                doc.State = TaskState.Parked;
                return "parked: Windows was shutting down";
            }).ConfigureAwait(false);
            if (record is not null)
                lock (parked) parked.Add(record);
        })).ToArray();

        try
        {
            if (!Task.WaitAll(writes, WriteBudget)) log.Warn("shutdown: not every task was parked in time");
        }
        catch (AggregateException ex)
        {
            log.Error("shutdown: parking a task failed", ex);
        }

        List<ShutdownParkedTask> done;
        lock (parked) done = [.. parked];
        if (done.Count == 0) return;
        ledger.Shutdown = new ShutdownStamp(now, done);
        log.Info($"shutdown: parked {string.Join(", ", done.Select(t => t.TaskId))}");
        _cancelCheck?.Dispose();
        _cancelCheck = new Timer(_ => ShutdownCancelled(), null, CancelledAfter, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Still running well after the shutdown request: it was cancelled. Puts back every task the shutdown
    /// parked that still carries its automatic note; anything changed since is left alone.</summary>
    public void ShutdownCancelled()
    {
        if (ledger.Shutdown is not { } stamp) return;
        foreach (var t in stamp.Tasks)
        {
            try
            {
                tasks.UpdateAsync(t.TaskId, Author, doc =>
                {
                    if (doc.State != TaskState.Parked || doc.Park?.Note != t.AutoNote) return null;
                    doc.State = Enum.TryParse<TaskState>(t.PreviousState, ignoreCase: true, out var state) ? state : TaskState.Active;
                    doc.Park = t.PreviousParkNote is { } note ? new ParkNote(t.PreviousParkAt ?? DateTimeOffset.UtcNow, note) : null;
                    return "unparked: the shutdown was cancelled";
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is KeyNotFoundException or TaskFormatException or IOException)
            {
                log.Warn($"shutdown: couldn't put {t.TaskId} back: {ex.Message}");
            }
        }
        ledger.Shutdown = null;
        log.Info("shutdown: cancelled; tasks put back");
    }

    /// <summary>At start: a stamp left from the last run means the shutdown went ahead. The tasks stay parked.</summary>
    public void ClearAtStart()
    {
        if (ledger.Shutdown is not { } stamp) return;
        log.Info($"the last run ended in a shutdown at {stamp.At:O}; {stamp.Tasks.Count} task(s) were parked");
        ledger.Shutdown = null;
    }

    internal static string AutoNote(IReadOnlyList<string> agents, string? lastTicked)
    {
        var who = agents.Count switch
        {
            1 => agents[0],
            _ => string.Join(", ", agents.Take(agents.Count - 1)) + " and " + agents[^1],
        };
        var verb = agents.Count == 1 ? "was" : "were";
        return $"Ochre was shut down while {who} {verb} running" + (lastTicked is null ? "." : $"; last step ticked: {lastTicked}.");
    }

    // The most recent tick in the log, by anyone, named by its text.
    private static string? LastTicked(TaskDocument doc)
    {
        foreach (var entry in doc.Log.Reverse())
            if (TickedLine().Match(entry.Text) is { Success: true } m)
                return doc.FindItem(m.Groups[1].Value)?.Text ?? m.Groups[1].Value;
        return null;
    }

    [GeneratedRegex(@"(?<![\w-])checked ([\w-]+)\s*$")]
    private static partial Regex TickedLine();
}
