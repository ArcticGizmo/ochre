using Ochre.Core;
using Ochre.Core.Claude;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Xunit;

namespace Ochre.Tests;

/// <summary>The agent write rules (security.md threat 3): bound task only, logged per session, no way to change state.</summary>
public sealed class AgentCommandsTests : IDisposable
{
    private const string Bound = "11111111-aaaa-bbbb-cccc-000000000001";
    private const string Stranger = "22222222-aaaa-bbbb-cccc-000000000002";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ochre-tests", Guid.NewGuid().ToString("N"));
    private readonly AppLog _log;
    private readonly TaskStore _store;
    private readonly AgentCommands _agents;

    public AgentCommandsTests()
    {
        _log = new AppLog(Path.Combine(_root, "logs"));
        _store = new TaskStore(Path.Combine(_root, "tasks"), _log);
        _store.Start();
        _agents = new AgentCommands(_store);
    }

    private async Task<string> NewTask(string title = "Task")
    {
        return await _store.CreateAsync(title, "user", doc =>
        {
            doc.AddItem("First step");
            doc.AddSession(new TaskSessionRef(Bound, @"C:\repo", null, "launched", null));
        });
    }

    private Task<string> Run(string? session, string? task, string op, params string[] args) =>
        _agents.HandleAsync(new AgentRequest(session, task, op, args));

    [Fact]
    public async Task A_bound_session_can_tick_add_and_note_and_each_write_is_logged_against_it()
    {
        var id = await NewTask();

        Assert.Equal("checked p1", await Run(Bound, id, "check", "p1"));
        Assert.Equal("added p2", await Run(Bound, id, "add-step", "Second step"));
        Assert.Equal("added p3", await Run(Bound, id, "add-step", "Nested", "--under", "p2"));
        Assert.Equal("noted", await Run(Bound, id, "note", "Found the cause."));

        var doc = _store.Get(id)!;
        Assert.True(doc.FindItem("p1")!.Done);
        Assert.Equal("Nested", Assert.Single(doc.FindItem("p2")!.Children).Text);
        Assert.Contains("Found the cause.", doc.Notes, StringComparison.Ordinal);
        var agentLines = doc.Log.Where(l => l.Text.Contains("session 11111111", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, agentLines.Count);
    }

    [Fact]
    public async Task A_session_not_bound_to_the_task_can_neither_read_nor_write_it()
    {
        var id = await NewTask();
        var before = await File.ReadAllTextAsync(Path.Combine(_root, "tasks", id + ".md"));

        Assert.StartsWith("error:", await Run(Stranger, id, "show"));
        Assert.StartsWith("error:", await Run(Stranger, id, "check", "p1"));
        Assert.StartsWith("error:", await Run(Stranger, id, "add-step", "Sneaky"));
        Assert.StartsWith("error:", await Run(Stranger, id, "note", "Sneaky"));

        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(_root, "tasks", id + ".md")));
    }

    [Fact]
    public async Task A_bound_session_cannot_reach_another_task_by_naming_it()
    {
        var mine = await NewTask("Mine");
        var other = await _store.CreateAsync("Other", "user", doc => doc.AddItem("Theirs"));

        Assert.StartsWith("error:", await Run(Bound, other, "check", "p1"));
        Assert.False(_store.Get(other)!.FindItem("p1")!.Done);
        Assert.Equal("checked p1", await Run(Bound, mine, "check", "p1"));
    }

    [Fact]
    public async Task Without_a_session_or_task_in_the_environment_nothing_runs()
    {
        var id = await NewTask();
        Assert.StartsWith("error:", await Run(null, id, "check", "p1"));
        Assert.StartsWith("error:", await Run(Bound, null, "check", "p1"));
        Assert.StartsWith("error:", await Run(Bound, "../T-0001", "check", "p1"));
        Assert.False(_store.Get(id)!.FindItem("p1")!.Done);
    }

    [Theory]
    [InlineData("set-state", "done")]
    [InlineData("state", "done")]
    [InlineData("done")]
    [InlineData("remove-link", "l1")]
    [InlineData("delete")]
    public async Task There_is_no_command_that_changes_state_or_removes_anything(string op, params string[] args)
    {
        var id = await NewTask();
        var before = await File.ReadAllTextAsync(Path.Combine(_root, "tasks", id + ".md"));

        Assert.StartsWith("error:", await Run(Bound, id, op, args));
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(_root, "tasks", id + ".md")));
    }

    [Fact]
    public async Task Unbinding_a_session_stops_its_next_write()
    {
        var id = await NewTask();
        Assert.Equal("checked p1", await Run(Bound, id, "check", "p1"));

        // The user removes the binding in an editor; the store hasn't reloaded yet, but writes read the file.
        var path = Path.Combine(_root, "tasks", id + ".md");
        var text = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, text.Replace(Bound, Stranger, StringComparison.Ordinal));

        Assert.StartsWith("error:", await Run(Bound, id, "uncheck", "p1"));
        Assert.True(TaskDocument.Parse(await File.ReadAllTextAsync(path)).FindItem("p1")!.Done);
    }

    [Fact]
    public async Task Over_long_text_is_refused()
    {
        var id = await NewTask();
        Assert.StartsWith("error:", await Run(Bound, id, "add-step", new string('x', AgentCommands.MaxStep + 1)));
        Assert.StartsWith("error:", await Run(Bound, id, "note", new string('x', AgentCommands.MaxNote + 1)));
        Assert.StartsWith("error:", await Run(Bound, id, "add-step", "   "));
    }

    [Fact]
    public void A_request_survives_the_trip_through_json()
    {
        var sent = new AgentRequest(Bound, "T-0001", "add-step", ["Say \"hi\"; then go", "--under", "p1"]);
        var got = AgentRequest.Parse(sent.ToJson())!;
        Assert.Equal(sent.SessionId, got.SessionId);
        Assert.Equal(sent.TaskId, got.TaskId);
        Assert.Equal(sent.Op, got.Op);
        Assert.Equal(sent.Args, got.Args);
        Assert.Null(AgentRequest.Parse("""{"op":"show","args":[1]}"""));
        Assert.Null(AgentRequest.Parse("not json"));
    }

    [Fact]
    public void The_agent_prompt_can_ride_the_command_line()
    {
        var note = AgentPrompt.For("T-0042", "ochre-cli");
        Assert.NotNull(note);
        Assert.True(ClaudeCli.IsSafePrompt(note));
        Assert.Contains("ochre-cli task show", note, StringComparison.Ordinal);
        Assert.Null(AgentPrompt.For("T-0042\" & calc", "ochre"));
        Assert.Null(AgentPrompt.For("T-0042", "ochre;calc"));
    }

    [Fact]
    public void Only_the_task_commands_are_pre_allowed()
    {
        Assert.Equal(["Bash(ochre task *)", "PowerShell(ochre task *)"], AgentPrompt.AllowedTools("ochre"));
        Assert.Empty(AgentPrompt.AllowedTools("ochre\" *"));
        Assert.Empty(AgentPrompt.AllowedTools(""));
    }

    public void Dispose()
    {
        _store.Dispose();
        _log.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
