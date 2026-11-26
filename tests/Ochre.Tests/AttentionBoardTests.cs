using Ochre.Core;
using Ochre.Core.Attention;
using Ochre.Core.Claude;
using Ochre.Core.Claude.Control;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// The attention inbox's model (ADR 0013) over the real <see cref="SessionManager"/> with an in-memory <c>claude</c>:
/// what raises an item, what clears it, and that ids the board never listed are refused.
/// </summary>
public sealed class AttentionBoardTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private const string Outside = "33333333-aaaa-bbbb-cccc-000000000003";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ochre-tests", Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly AppLog _log;
    private readonly TaskStore _store;
    private readonly FakeClaudeLauncher _claude = new();
    private readonly List<LiveClaudeSession> _outside = [];
    private readonly ManualClock _clock = new();
    private readonly SessionManager _sessions;
    private readonly AttentionBoard _board;

    public AttentionBoardTests()
    {
        _repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        _log = new AppLog(Path.Combine(_root, "logs"));
        _store = new TaskStore(Path.Combine(_root, "tasks"), _log);
        _store.Start();
        var system = new SessionSystem
        {
            Launcher = _claude,
            LocateCli = () => @"C:\fake\bin\ochre.com",
            IsTrusted = _ => true,
            GrantTrust = _ => true,
            FindLive = _ => null,
            ReadLive = () => [.. _outside],
            ResolveTranscript = (_, _) => null,
            FolderExists = Directory.Exists,
        };
        _sessions = new SessionManager(_store, new SessionLedger(Path.Combine(_root, "sessions.json")), _log, OchreProfile.Current, system);
        _board = new AttentionBoard(_sessions, _clock);
    }

    private async Task<(LiveSession Session, FakeClaudeProcess Process)> Start(string? taskId = null, string role = "alpha")
    {
        taskId ??= await _store.CreateAsync("Task", "user", _ => { });
        var (s, _) = await _sessions.StartAsync(taskId, _repo, role, null, "go", trustFolder: false);
        return (s!, _claude.Last);
    }

    private static void Until(Func<bool> condition, string what) =>
        Assert.True(SpinWait.SpinUntil(condition, Wait), $"timed out waiting for {what}");

    private static T Locked<T>(LiveSession s, Func<SessionConversation, T> read)
    {
        lock (s.Gate) return read(s.Conversation);
    }

    private IReadOnlyList<AttentionItem> Items()
    {
        _board.Refresh();
        return _board.Items;
    }

    private static void FinishTurn(LiveSession s, FakeClaudeProcess p, string reply)
    {
        var before = Locked(s, c => c.LastTurn);
        p.Emit(StreamJson.Text(reply));
        p.Emit(StreamJson.Result());
        Until(() => Locked(s, c => c.LastTurn is { } t && !ReferenceEquals(t, before)), "the turn to end");
    }

    [Fact]
    public async Task A_permission_needs_you_until_it_is_answered()
    {
        var (s, p) = await Start();
        p.Emit(StreamJson.AskPermission("req-1", "git push"));
        Until(() => Locked(s, c => c.PendingPermission is not null), "the permission");

        var item = Assert.Single(Items());
        Assert.Equal((AttentionKind.Permission, "req-1", s.Id), (item.Kind, item.RequestId, item.SessionId));
        Assert.True(item.NeedsYou);
        Assert.Equal("wants to use Bash", item.Title);
        Assert.Contains("git push", item.Detail, StringComparison.Ordinal);
        Assert.Equal("alpha", item.Role);

        Assert.True(_sessions.Respond(s, "req-1", allow: true, null, null));
        Assert.Empty(Items());   // the turn carries on; nothing has finished yet
    }

    [Fact]
    public async Task A_finished_turn_waits_until_seen_and_the_next_turn_raises_a_new_one()
    {
        var (s, p) = await Start();
        Assert.Empty(Items());   // mid-turn

        FinishTurn(s, p, "Looked at it.\nAll 12 tests pass.");
        var first = Assert.Single(Items());
        Assert.Equal(AttentionKind.Finished, first.Kind);
        Assert.False(first.NeedsYou);
        Assert.Equal("All 12 tests pass.", first.Detail);

        Assert.True(_board.Seen(s.Id));
        Assert.Empty(Items());
        Assert.False(_board.Seen(s.Id));   // nothing left to see

        _sessions.Send(s, "and the docs?");
        Assert.Empty(Items());
        FinishTurn(s, p, "Docs updated.");
        var second = Assert.Single(Items());
        Assert.Equal("Docs updated.", second.Detail);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Sending_again_without_looking_drops_the_old_finished_turn()
    {
        var (s, p) = await Start();
        FinishTurn(s, p, "Done.");
        Assert.Single(Items());

        _sessions.Send(s, "next");
        Assert.Empty(Items());
    }

    [Fact]
    public async Task A_turn_that_errors_or_a_crash_is_failed_and_a_clean_exit_is_not()
    {
        var (s, p) = await Start();
        p.Emit("""{"type":"result","subtype":"error_during_execution","is_error":true,"total_cost_usd":0.01,"duration_ms":5,"usage":{"input_tokens":10,"output_tokens":2}}""");
        Until(() => Locked(s, c => c.LastTurn is not null), "the result");
        var failed = Assert.Single(Items());
        Assert.Equal(AttentionKind.Failed, failed.Kind);
        Assert.Contains("error_during_execution", failed.Detail, StringComparison.Ordinal);

        _board.Seen(s.Id);
        p.Exit(0);
        Until(() => s.Dormant, "the exit");
        Assert.Empty(Items());

        var (crashed, cp) = await Start();
        cp.Exit(3);
        Until(() => crashed.Dormant, "the crash");
        var crash = Assert.Single(Items());
        Assert.Equal((AttentionKind.Failed, crashed.Id), (crash.Kind, crash.SessionId));
        Assert.Contains("(3)", crash.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_outside_session_waiting_is_listed_and_a_dismissal_lasts_until_it_waits_again()
    {
        var taskId = await _store.CreateAsync("Task", "user", _ => { });
        _outside.Add(new LiveClaudeSession(4242, Outside, _repo, "waiting", "cli", null));
        _sessions.ScanObserved();
        await _sessions.BindAsync(_sessions.Get(Outside)!, taskId);

        var waiting = Assert.Single(Items());
        Assert.Equal((AttentionKind.Waiting, taskId), (waiting.Kind, waiting.TaskId));
        Assert.True(waiting.NeedsYou);
        Assert.Null(waiting.RequestId);

        Assert.True(_board.Dismiss(waiting.Id));
        Assert.Empty(Items());
        _sessions.ScanObserved();
        Assert.Empty(Items());   // still the same wait

        _outside[0] = _outside[0] with { Status = "busy" };
        _sessions.ScanObserved();
        Assert.Empty(Items());
        _outside[0] = _outside[0] with { Status = "waiting" };
        _sessions.ScanObserved();
        Assert.NotEqual(waiting.Id, Assert.Single(Items()).Id);
    }

    [Fact]
    public async Task Ids_the_board_never_listed_and_prompts_that_must_be_answered_are_refused()
    {
        var (s, p) = await Start();
        p.Emit(StreamJson.AskPermission("req-1", "git push"));
        Until(() => Locked(s, c => c.PendingPermission is not null), "the permission");
        var permission = Assert.Single(Items());

        Assert.False(_board.Dismiss(permission.Id));
        Assert.False(_board.Dismiss("not-an-item"));
        Assert.False(_board.Dismiss($"{s.Id}/999"));
        Assert.False(_board.Seen("no-such-session"));
        Assert.Null(_board.Get("not-an-item"));
        Assert.Same(permission, _board.Get(permission.Id));
        Assert.Single(Items());
    }

    [Fact]
    public async Task Tasks_with_something_blocked_come_first_then_oldest_first()
    {
        var (done, dp) = await Start(role: "done");
        FinishTurn(done, dp, "Finished.");
        _board.Refresh();
        _clock.Advance(TimeSpan.FromMinutes(1));

        var (asking, ap) = await Start(role: "asking");
        ap.Emit(StreamJson.AskPermission("req-1", "git push"));
        Until(() => Locked(asking, c => c.PendingPermission is not null), "the permission");

        var order = Items().Select(i => (i.Role, i.Kind)).ToList();
        Assert.Equal([("asking", AttentionKind.Permission), ("done", AttentionKind.Finished)], order);
    }

    [Fact]
    public async Task An_item_keeps_its_id_and_age_while_it_lasts_and_changes_are_raised_once()
    {
        var raised = 0;
        _board.Changed += () => raised++;
        var (s, p) = await Start();
        p.Emit(StreamJson.AskPermission("req-1", "git push"));
        Until(() => Locked(s, c => c.PendingPermission is not null), "the permission");

        var first = Assert.Single(Items());
        _clock.Advance(TimeSpan.FromMinutes(5));
        var again = Assert.Single(Items());
        Assert.Equal((first.Id, first.Since), (again.Id, again.Since));
        Assert.Equal(1, raised);
    }

    public void Dispose()
    {
        _board.Dispose();
        _sessions.Dispose();
        _store.Dispose();
        _log.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
