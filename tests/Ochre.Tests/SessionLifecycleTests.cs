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

    public void Dispose()
    {
        foreach (var m in _managers) m.Dispose();
        _store.Dispose();
        _log.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
