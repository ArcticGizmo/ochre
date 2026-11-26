using Ochre.Core;
using Ochre.Core.Claude;
using Ochre.Core.Claude.Control;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// A launched agent's life (start, output, prompts, exit, dormant, resume, restart) driven through the real
/// <see cref="SessionManager"/> and controller with an in-memory <c>claude</c>. Nothing here starts a process or reads
/// the user's <c>~/.claude</c>: trust, live sessions and transcripts come from the test.
/// </summary>
public sealed class SessionLifecycleTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ochre-tests", Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly AppLog _log;
    private readonly TaskStore _store;
    private readonly FakeClaudeLauncher _claude = new();
    private readonly HashSet<string> _trusted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _transcripts = new(StringComparer.Ordinal);
    private LiveClaudeSession? _liveElsewhere;
    private readonly List<LiveClaudeSession> _outside = [];
    private readonly List<SessionManager> _managers = [];

    public SessionLifecycleTests()
    {
        _repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        _trusted.Add(_repo);
        _log = new AppLog(Path.Combine(_root, "logs"));
        _store = new TaskStore(Path.Combine(_root, "tasks"), _log);
        _store.Start();
    }

    private SessionManager NewManager()
    {
        var system = new SessionSystem
        {
            Launcher = _claude,
            LocateCli = () => @"C:\fake\bin\ochre.com",
            IsTrusted = cwd => _trusted.Contains(cwd),
            GrantTrust = cwd => _trusted.Add(cwd),
            FindLive = id => _liveElsewhere is { } live && live.SessionId == id ? live : null,
            ReadLive = () => [.. _outside],
            ResolveTranscript = (id, _) => _transcripts.GetValueOrDefault(id),
            FolderExists = Directory.Exists,
        };
        var manager = new SessionManager(_store, new SessionLedger(Path.Combine(_root, "sessions.json")), _log, OchreProfile.Current, system);
        _managers.Add(manager);
        return manager;
    }

    private Task<string> NewTask() => _store.CreateAsync("Task", "user", doc => doc.AddItem("First step"));

    private static void Until(Func<bool> condition, string what) =>
        Assert.True(SpinWait.SpinUntil(condition, Wait), $"timed out waiting for {what}");

    private static T Locked<T>(LiveSession s, Func<SessionConversation, T> read)
    {
        lock (s.Gate) return read(s.Conversation);
    }

    [Fact]
    public async Task Starting_binds_the_agent_to_its_task_and_sends_the_first_message()
    {
        var sessions = NewManager();
        var taskId = await NewTask();

        var (s, refusal) = await sessions.StartAsync(taskId, _repo, "alpha", "acceptEdits", "Read the task.", trustFolder: false);

        Assert.Equal(StartRefusal.None, refusal);
        var (launch, process) = Assert.Single(_claude.Launches);
        Assert.Contains($"--session-id {s!.Id}", launch.Args, StringComparison.Ordinal);
        Assert.Contains("--permission-mode acceptEdits", launch.Args, StringComparison.Ordinal);
        Assert.Contains("\"Bash(ochre task *)\"", launch.Args, StringComparison.Ordinal);
        Assert.Contains("--append-system-prompt", launch.Args, StringComparison.Ordinal);
        Assert.Equal(_repo, launch.Cwd);
        Assert.Equal(taskId, launch.Environment["OCHRE_TASK"]);
        Assert.Equal(s.Id, launch.Environment["OCHRE_SESSION"]);
        Assert.StartsWith(@"C:\fake\bin" + Path.PathSeparator, launch.Environment["PATH"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["Read the task."], process.Prompts);

        var bound = Assert.Single(_store.Get(taskId)!.Sessions);
        Assert.Equal((s.Id, "launched", "alpha"), (bound.Id, bound.Binding, bound.Role));
    }

    [Fact]
    public async Task The_kernel_works_with_every_module_off()
    {
        // ADR 0007: no module, and no platform service a module would need, is required for tasks and agents.
        var sessions = NewManager();
        var ledger = new SessionLedger(Path.Combine(_root, "sessions.json"));
        using var modules = new Ochre.Core.Modules.ModuleHost(
            [new Ochre.Modules.Terminals.TerminalsModule()],
            _ => false,
            new Ochre.Core.Modules.ModuleKernel(_log, _root, _store, sessions, ledger, new Dictionary<Type, object>(), _ => { }));
        modules.StartAll();
        Assert.Empty(modules.RunningIds);

        var taskId = await NewTask();
        var (s, refusal) = await sessions.StartAsync(taskId, _repo, "alpha", null, "Read the task.", trustFolder: false);
        Assert.Equal(StartRefusal.None, refusal);
        Assert.NotNull(s);
        await _store.UpdateAsync(taskId, "user", doc =>
        {
            doc.AddItem("Works without modules", null);
            return "added a step";
        });
        Assert.Contains(_store.Get(taskId)!.Plan, i => i.Text == "Works without modules");
    }

    [Fact]
    public async Task An_untrusted_folder_is_asked_about_and_trusted_only_on_yes()
    {
        var sessions = NewManager();
        var taskId = await NewTask();
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;

        var (none, refusal) = await sessions.StartAsync(taskId, other, null, null, null, trustFolder: false);
        Assert.Null(none);
        Assert.Equal(StartRefusal.NeedsTrust, refusal);
        Assert.Empty(_claude.Launches);
        Assert.Empty(_store.Get(taskId)!.Sessions);

        var (s, _) = await sessions.StartAsync(taskId, other, null, null, null, trustFolder: true);
        Assert.NotNull(s);
        Assert.Contains(other, _trusted);
        Assert.Single(_claude.Launches);
    }

    [Fact]
    public async Task Output_folds_into_the_thread_and_only_changed_items_are_broadcast()
    {
        var sessions = NewManager();
        var (s, _) = await sessions.StartAsync(await NewTask(), _repo, null, null, "hi", false);
        var changes = new List<SessionChanges>();
        sessions.Changed += (_, c) => changes.Add(c);
        sessions.FlushNow();
        changes.Clear();

        _claude.Last.Emit(StreamJson.Init(s!.Id));
        _claude.Last.Emit(StreamJson.Text("Hello."));
        _claude.Last.Emit(StreamJson.Result());
        Until(() => Locked(s, c => c.LastTurn is not null), "the turn to finish");

        sessions.FlushNow();
        var change = Assert.Single(changes);
        Assert.False(change.Reset);
        Assert.Equal([1], change.Items);   // the assistant reply; the user prompt (index 0) didn't change
        var reply = Locked(s, c => Assert.IsType<AssistantMessageItem>(c.Items[1]));
        Assert.True(reply.IsComplete);
        Assert.Equal("Hello.", Assert.IsType<TextPart>(Assert.Single(reply.Parts)).Text);
    }

    [Fact]
    public async Task A_permission_is_answered_once_and_the_answer_reaches_the_cli()
    {
        var sessions = NewManager();
        var (s, _) = await sessions.StartAsync(await NewTask(), _repo, null, null, "go", false);

        _claude.Last.Emit(StreamJson.AskPermission("req-1", "git status"));
        Until(() => Locked(s!, c => c.PendingPermission is not null), "the permission card");

        Assert.False(sessions.Respond(s!, "req-other", allow: true, null, null));
        Assert.True(sessions.Respond(s!, "req-1", allow: true, null, null));
        Assert.False(sessions.Respond(s!, "req-1", allow: true, null, null));

        var answer = Assert.Single(_claude.Last.Sent, n => (string?)n["type"] == "control_response");
        Assert.Equal("req-1", (string?)answer["response"]?["request_id"]);
        Assert.Equal("allow", (string?)answer["response"]?["response"]?["behavior"]);
        Assert.Equal("git status", (string?)answer["response"]?["response"]?["updatedInput"]?["command"]);
        Assert.Null(Locked(s!, c => c.PendingPermission));
    }

    [Fact]
    public async Task A_denial_carries_the_users_message_and_an_allow_ignores_one()
    {
        var sessions = NewManager();
        var (s, _) = await sessions.StartAsync(await NewTask(), _repo, null, null, "go", false);

        _claude.Last.Emit(StreamJson.AskPermission("req-1", "git push"));
        Until(() => Locked(s!, c => c.PendingPermission is not null), "the first prompt");
        Assert.True(sessions.Respond(s!, "req-1", allow: false, null, null, "Push to a branch of your own instead."));

        _claude.Last.Emit(StreamJson.AskPermission("req-2", "git status"));
        Until(() => Locked(s!, c => c.PendingPermission?.Request.RequestId == "req-2"), "the second prompt");
        Assert.True(sessions.Respond(s!, "req-2", allow: true, null, null, "ignored"));

        var answers = _claude.Last.Sent.Where(n => (string?)n["type"] == "control_response").Select(n => n["response"]?["response"]).ToList();
        Assert.Equal(("deny", "Push to a branch of your own instead."), ((string?)answers[0]?["behavior"], (string?)answers[0]?["message"]));
        Assert.Equal("allow", (string?)answers[1]?["behavior"]);
        Assert.Null(answers[1]?["message"]);
    }

    [Fact]
    public async Task When_claude_exits_the_agent_goes_dormant_and_sending_resumes_the_same_session()
    {
        var sessions = NewManager();
        var (s, _) = await sessions.StartAsync(await NewTask(), _repo, null, "plan", "go", false);
        _claude.Last.Emit(StreamJson.Init(s!.Id, "plan"));
        _transcripts[s.Id] = Path.Combine(_root, "transcript.jsonl");

        _claude.Last.Exit(0);
        Until(() => s.Dormant, "the agent to go dormant");

        sessions.Send(s, "carry on");

        Assert.Equal(2, _claude.Launches.Count);
        var (resume, process) = _claude.Launches[1];
        Assert.Contains($"--resume {s.Id}", resume.Args, StringComparison.Ordinal);
        Assert.DoesNotContain("--session-id", resume.Args, StringComparison.Ordinal);
        Assert.Contains("--permission-mode plan", resume.Args, StringComparison.Ordinal);
        Assert.Equal(s.Id, resume.Environment["OCHRE_SESSION"]);
        Assert.Equal(["carry on"], process.Prompts);
        Assert.False(s.Dormant);
        Assert.Contains(Locked(s, c => c.Items.OfType<NoteItem>().Select(n => n.Text).ToList()), t => t == "resumed");
    }

    [Fact]
    public async Task An_agent_that_never_wrote_a_transcript_starts_fresh_under_the_same_id()
    {
        var sessions = NewManager();
        var (s, _) = await sessions.StartAsync(await NewTask(), _repo, null, null, null, false);
        _claude.Last.Exit(0);
        Until(() => s!.Dormant, "the agent to go dormant");

        sessions.Send(s!, "hello");

        Assert.Contains($"--session-id {s!.Id}", _claude.Launches[1].Launch.Args, StringComparison.Ordinal);
        Assert.DoesNotContain("--resume", _claude.Launches[1].Launch.Args, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_is_refused_while_the_session_is_open_in_another_claude()
    {
        var sessions = NewManager();
        var (s, _) = await sessions.StartAsync(await NewTask(), _repo, null, null, null, false);
        _claude.Last.Exit(0);
        Until(() => s!.Dormant, "the agent to go dormant");
        _liveElsewhere = new LiveClaudeSession(4242, s!.Id, _repo, "idle", "cli", null);

        var ex = Assert.Throws<InvalidOperationException>(() => sessions.Send(s, "hello"));
        Assert.Contains("4242", ex.Message, StringComparison.Ordinal);
        Assert.Single(_claude.Launches);
        Assert.True(s.Dormant);
    }

    [Fact]
    public async Task After_clear_a_resume_continues_the_new_conversation()
    {
        var sessions = NewManager();
        var (s, _) = await sessions.StartAsync(await NewTask(), _repo, null, null, "go", false);
        var cleared = Guid.NewGuid().ToString();
        _claude.Last.Emit(StreamJson.Init(s!.Id));
        _claude.Last.Emit(StreamJson.Init(cleared));   // what `/clear` looks like on the wire
        Until(() => s.ClaudeId == cleared, "the rebind");
        _transcripts[cleared] = Path.Combine(_root, "cleared.jsonl");

        _claude.Last.Exit(0);
        Until(() => s.Dormant, "the agent to go dormant");
        sessions.Send(s, "and now?");

        Assert.Contains($"--resume {cleared}", _claude.Launches[1].Launch.Args, StringComparison.Ordinal);
        Assert.Equal(s.Id, _claude.Launches[1].Launch.Environment["OCHRE_SESSION"]);   // the task binding is the launch id
    }

    [Fact]
    public async Task After_a_restart_agents_come_back_dormant_with_their_history_and_mode()
    {
        var first = NewManager();
        var taskId = await NewTask();
        var (s, _) = await first.StartAsync(taskId, _repo, "beta", "plan", null, false);
        var transcript = Path.Combine(_root, "beta.jsonl");
        await File.WriteAllLinesAsync(transcript,
        [
            """{"type":"user","uuid":"u1","message":{"role":"user","content":"fix the rounding bug"}}""",
            """{"type":"assistant","uuid":"a1","parentUuid":"u1","message":{"role":"assistant","content":[{"type":"text","text":"Fixed it."}]}}""",
        ]);
        _transcripts[s!.Id] = transcript;
        first.Dispose();

        var second = NewManager();
        second.LoadDormant();
        var back = second.Get(s.Id);
        Assert.NotNull(back);
        Assert.True(back!.Dormant);
        Assert.Equal("beta", back.Role);

        await second.EnsureHistoryAsync(back);
        var texts = Locked(back, c => c.Items.Select(i => i switch
        {
            UserMessageItem u => "user: " + u.Text,
            AssistantMessageItem a => "assistant: " + string.Join("", a.Parts.OfType<TextPart>().Select(p => p.Text)),
            _ => i.GetType().Name,
        }).ToList());
        Assert.Equal(["user: fix the rounding bug", "assistant: Fixed it."], texts);

        second.Send(back, "anything left?");
        var resume = _claude.Launches[^1].Launch.Args;
        Assert.Contains($"--resume {s.Id}", resume, StringComparison.Ordinal);
        Assert.Contains("--permission-mode plan", resume, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dormant_agent_without_a_transcript_says_so_instead_of_failing()
    {
        var first = NewManager();
        var taskId = await NewTask();
        var (s, _) = await first.StartAsync(taskId, _repo, null, null, null, false);
        first.Dispose();

        var second = NewManager();
        second.LoadDormant();
        var back = second.Get(s!.Id)!;
        await second.EnsureHistoryAsync(back);

        var note = Locked(back, c => Assert.IsType<NoteItem>(Assert.Single(c.Items)));
        Assert.Equal(NoteKind.Info, note.Kind);
    }

    [Fact]
    public async Task Parking_with_no_running_agents_parks_with_your_note()
    {
        var sessions = NewManager();
        var parking = new ParkCoordinator(_store, sessions, new ParkRequests(), _log);
        var taskId = await NewTask();

        await await parking.ParkAsync(taskId, "Stopping here for today.");

        var doc = _store.Get(taskId)!;
        Assert.Equal(TaskState.Parked, doc.State);
        Assert.Equal("Stopping here for today.", doc.Park!.Note);
    }

    [Fact]
    public async Task Parking_asks_each_running_agent_for_a_note_then_stops_it()
    {
        var sessions = NewManager();
        var requests = new ParkRequests();
        var agents = new AgentCommands(_store, requests);
        var parking = new ParkCoordinator(_store, sessions, requests, _log);
        var taskId = await NewTask();
        var (alpha, _) = await sessions.StartAsync(taskId, _repo, "alpha", null, null, false);
        var alphaProcess = _claude.Last;
        var (beta, _) = await sessions.StartAsync(taskId, _repo, "beta", null, null, false);
        var betaProcess = _claude.Last;

        var collecting = await parking.ParkAsync(taskId, "Stopping for the day.");
        Assert.Equal(TaskState.Parked, _store.Get(taskId)!.State);
        Assert.Equal(2, parking.WaitingFor(taskId).Count);

        // Each agent is asked, writes its note through the CLI as it would, and ends its turn.
        foreach (var (s, p, note) in new[] { (alpha!, alphaProcess, "Parser fixed."), (beta!, betaProcess, "Tests half written.") })
        {
            Until(() => p.Prompts.Any(t => t.Contains("park-note", StringComparison.Ordinal)), $"{s.Role}'s park prompt");
            Assert.Equal("park note saved", await agents.HandleAsync(new AgentRequest(s.Id, taskId, "park-note", [note])));
            p.Emit(StreamJson.Result());
        }
        await collecting.WaitAsync(Wait);

        // Stopping closes the CLI's input; the agent goes dormant once the process has exited.
        Until(() => alpha!.Dormant && beta!.Dormant, "both agents to go dormant");
        Assert.Empty(parking.WaitingFor(taskId));
        var lines = _store.Get(taskId)!.Park!.Note.Split('\n');
        Assert.Equal("Stopping for the day.", lines[0]);
        Assert.Contains("- alpha: Parser fixed.", lines);
        Assert.Contains("- beta: Tests half written.", lines);
    }

    [Fact]
    public async Task A_busy_agent_is_interrupted_and_one_that_never_answers_is_stopped_anyway()
    {
        var sessions = NewManager();
        var requests = new ParkRequests();
        var parking = new ParkCoordinator(_store, sessions, requests, _log, busyWait: TimeSpan.FromMilliseconds(300), noteWait: TimeSpan.FromMilliseconds(300));
        var taskId = await NewTask();
        var (s, _) = await sessions.StartAsync(taskId, _repo, "alpha", null, "long job", false);
        var process = _claude.Last;   // mid-turn: a prompt went in and no result has come back

        var collecting = await parking.ParkAsync(taskId, null);
        Until(() => process.Sent.Any(n => (string?)n["request"]?["subtype"] == "interrupt"), "the interrupt");
        process.Emit(StreamJson.Result());
        await collecting.WaitAsync(Wait);

        Assert.Contains(process.Prompts, t => t.Contains("park-note", StringComparison.Ordinal));
        Until(() => s!.Dormant, "the agent to go dormant");
        Assert.False(requests.IsOpen(taskId, s!.Id));   // its chance to write a note has closed
        Assert.Equal("", _store.Get(taskId)!.Park!.Note);
    }

    private const string Outside = "33333333-aaaa-bbbb-cccc-000000000003";

    private LiveClaudeSession OutsideSession(string status = "busy") => new(4242, Outside, _repo, status, "cli", null);

    [Fact]
    public async Task A_session_started_outside_Ochre_is_listed_read_only_and_suggested_for_the_task_whose_repo_holds_it()
    {
        var sessions = NewManager();
        var taskId = await _store.CreateAsync("Task", "user", doc => doc.SetRepos([_repo]));
        _outside.Add(OutsideSession());

        sessions.ScanObserved();

        var s = sessions.Get(Outside)!;
        Assert.True(s.Observed);
        Assert.Equal("", s.TaskId);
        Assert.Equal("busy", s.ObservedStatus);
        var suggestion = Assert.Single(sessions.Suggest(s));
        Assert.Equal(taskId, suggestion.TaskId);
        Assert.Throws<InvalidOperationException>(() => sessions.Send(s, "hello"));
        Assert.Throws<InvalidOperationException>(() => sessions.Stop(s));
        Assert.Empty(_claude.Launches);
    }

    [Fact]
    public async Task Linking_records_it_as_confirmed_and_a_linked_session_outlives_its_process()
    {
        var sessions = NewManager();
        var taskId = await NewTask();
        _outside.Add(OutsideSession());
        sessions.ScanObserved();

        await sessions.BindAsync(sessions.Get(Outside)!, taskId);
        var bound = Assert.Single(_store.Get(taskId)!.Sessions);
        Assert.Equal((Outside, "confirmed"), (bound.Id, bound.Binding));
        Assert.Empty(sessions.Suggest(sessions.Get(Outside)!));

        _outside.Clear();
        sessions.ScanObserved();
        Assert.Equal("closed", sessions.Get(Outside)!.ObservedStatus);

        // A restart brings it back closed from the task; an unlinked one whose process went is simply dropped.
        var restarted = NewManager();
        restarted.LoadDormant();
        Assert.True(restarted.Get(Outside)!.Observed);
        await restarted.UnbindAsync(restarted.Get(Outside)!);
        Assert.Null(restarted.Get(Outside));
        Assert.Empty(_store.Get(taskId)!.Sessions);
    }

    [Fact]
    public async Task An_observed_thread_follows_its_transcript_and_picks_up_the_branch()
    {
        var sessions = NewManager();
        var path = Path.Combine(_root, "outside.jsonl");
        static string Prompt(string text, string branch) =>
            $$"""{"type":"user","message":{"role":"user","content":"{{text}}"},"uuid":"{{Guid.NewGuid()}}","gitBranch":"{{branch}}"}""" + "\n";
        await File.WriteAllTextAsync(path, Prompt("first", "main"));
        _transcripts[Outside] = path;
        _outside.Add(OutsideSession("idle"));

        sessions.ScanObserved();
        var s = sessions.Get(Outside)!;
        Assert.Equal("main", s.Branch);
        await sessions.EnsureHistoryAsync(s);
        Assert.Equal(1, Locked(s, c => c.Items.OfType<UserMessageItem>().Count()));

        await File.AppendAllTextAsync(path, Prompt("second", "feature/PROJ-7-x"));
        sessions.ScanObserved();
        Assert.Equal(["first", "second"], Locked(s, c => c.Items.OfType<UserMessageItem>().Select(u => u.Text).ToList()));
        Assert.Equal("feature/PROJ-7-x", s.Branch);
    }

    [Fact]
    public async Task An_agent_Ochre_launched_is_never_taken_for_an_outside_session()
    {
        var sessions = NewManager();
        var (s, _) = await sessions.StartAsync(await NewTask(), _repo, null, null, null, trustFolder: false);
        _outside.Add(new LiveClaudeSession(4243, s!.Id, _repo, "busy", "sdk-cli", null));

        sessions.ScanObserved();

        Assert.False(sessions.Get(s.Id)!.Observed);
        Assert.Single(sessions.All);
    }

    private (SessionManager Sessions, SessionLedger Ledger, ShutdownParker Parker) NewShutdownSetup(bool enabled)
    {
        var ledger = new SessionLedger(Path.Combine(_root, "sessions.json"));
        var sessions = NewManager();
        return (sessions, ledger, new ShutdownParker(_store, sessions, ledger, () => enabled, _log));
    }

    [Fact]
    public async Task With_auto_park_off_a_shutdown_changes_nothing()
    {
        var (sessions, ledger, parker) = NewShutdownSetup(enabled: false);
        var taskId = await NewTask();
        await _store.UpdateAsync(taskId, "user", doc => { doc.State = TaskState.Active; return "set state active"; });
        await sessions.StartAsync(taskId, _repo, "alpha", null, null, trustFolder: false);

        parker.OnSessionEnding();

        Assert.Equal(TaskState.Active, _store.Get(taskId)!.State);
        Assert.Null(ledger.Shutdown);
    }

    [Fact]
    public async Task A_shutdown_parks_tasks_with_running_agents_with_an_automatic_note_and_stamps_the_ledger()
    {
        var (sessions, ledger, parker) = NewShutdownSetup(enabled: true);
        var busy = await NewTask();
        await _store.UpdateAsync(busy, "user", doc =>
        {
            doc.State = TaskState.Active;
            doc.SetItemDone("p1", true);
            return "checked p1";
        });
        var quiet = await NewTask();
        await _store.UpdateAsync(quiet, "user", doc => { doc.State = TaskState.Active; return "set state active"; });
        await sessions.StartAsync(busy, _repo, "alpha", null, null, trustFolder: false);
        await sessions.StartAsync(busy, _repo, "beta", null, null, trustFolder: false);

        parker.OnSessionEnding();

        var doc = _store.Get(busy)!;
        Assert.Equal(TaskState.Parked, doc.State);
        Assert.Equal("Ochre was shut down while alpha and beta were running; last step ticked: First step.", doc.Park!.Note);
        Assert.Equal(TaskState.Active, _store.Get(quiet)!.State);
        var stamped = Assert.Single(ledger.Shutdown!.Tasks);
        Assert.Equal((busy, "active"), (stamped.TaskId, stamped.PreviousState));
        // Agents are left to end with the system rather than stopped.
        Assert.All(_claude.Launches, l => Assert.False(l.Process.HasExited));
    }

    [Fact]
    public async Task A_cancelled_shutdown_puts_tasks_back_but_leaves_alone_any_the_user_changed_since()
    {
        var (sessions, ledger, parker) = NewShutdownSetup(enabled: true);
        var untouched = await NewTask();
        var resumed = await NewTask();
        foreach (var id in new[] { untouched, resumed })
        {
            await _store.UpdateAsync(id, "user", doc =>
            {
                doc.State = TaskState.Active;
                doc.Park = new ParkNote(DateTimeOffset.UtcNow.AddDays(-1), "Yesterday's note");
                return "set state active";
            });
            await sessions.StartAsync(id, _repo, "alpha", null, null, trustFolder: false);
        }
        parker.OnSessionEnding();
        await _store.UpdateAsync(resumed, "user", doc => { doc.State = TaskState.Active; return "set state active"; });

        parker.ShutdownCancelled();

        var back = _store.Get(untouched)!;
        Assert.Equal(TaskState.Active, back.State);
        Assert.Equal("Yesterday's note", back.Park!.Note);
        Assert.StartsWith("Ochre was shut down", _store.Get(resumed)!.Park!.Note, StringComparison.Ordinal);
        Assert.Null(ledger.Shutdown);
    }

    [Fact]
    public async Task A_stamp_found_at_start_means_the_shutdown_went_ahead_and_the_tasks_stay_parked()
    {
        var (sessions, _, parker) = NewShutdownSetup(enabled: true);
        var taskId = await NewTask();
        await _store.UpdateAsync(taskId, "user", doc => { doc.State = TaskState.Active; return "set state active"; });
        await sessions.StartAsync(taskId, _repo, "alpha", null, null, trustFolder: false);
        parker.OnSessionEnding();

        var (_, ledger, next) = NewShutdownSetup(enabled: true);
        Assert.NotNull(ledger.Shutdown);
        next.ClearAtStart();

        Assert.Null(ledger.Shutdown);
        Assert.Equal(TaskState.Parked, _store.Get(taskId)!.State);
    }

    public void Dispose()
    {
        foreach (var m in _managers) m.Dispose();
        _store.Dispose();
        _log.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
