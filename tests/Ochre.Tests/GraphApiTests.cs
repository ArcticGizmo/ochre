using System.Text.Json;
using Ochre.Core;
using Ochre.Core.Bridge;
using Ochre.Core.Claude;
using Ochre.Core.Tasks;
using Ochre.Modules.Git;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// The renderer is untrusted, so the graph's messages only ever run git in a task's own folders, on commits and
/// worktrees the graph showed, and with a base branch git itself listed. Runs against a real repo.
/// </summary>
public sealed class GraphApiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ochre-graph-").FullName;
    private readonly string? _git = ExecutableResolver.Find("git");
    private readonly AppLog _log;
    private readonly TaskStore _store;
    private readonly GraphLoads _loads = new();

    public GraphApiTests()
    {
        _log = new AppLog(Path.Combine(_dir, "logs"));
        _store = new TaskStore(Path.Combine(_dir, "tasks"), _log);
    }

    public void Dispose()
    {
        _store.Dispose();
        _log.Dispose();
        GitRunnerTests.DeleteTree(_dir);
    }

    private void Git(string cwd, params string[] args) => GitRunnerTests.Git(_git!, cwd, args);

    private string Repo(string name)
    {
        var repo = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "config", "user.email", "t@example.com");
        Git(repo, "config", "user.name", "t");
        Git(repo, "config", "commit.gpgsign", "false");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "hello\n");
        Git(repo, "add", "-A");
        Git(repo, "commit", "-q", "-m", "First line", "-m", "The body.");
        return repo;
    }

    private async Task<string> Task(params string[] repos) =>
        await _store.CreateAsync("graph test", "user", d => d.SetRepos(repos));

    private async Task<(bool Ok, JsonElement Result, string? Error)> Call(string type, object payload)
    {
        string? reply = null;
        var bridge = new BridgeDispatcher(m => reply = m);
        GraphApi.Register(bridge, _store, _loads);
        await bridge.DispatchAsync(JsonSerializer.Serialize(new { id = 1, type, payload }));
        var root = JsonDocument.Parse(reply!).RootElement.Clone();
        var ok = root.GetProperty("ok").GetBoolean();
        return (ok, ok ? root.GetProperty("result") : default, ok ? null : root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_graph_loads_only_for_one_of_the_tasks_own_folders()
    {
        if (_git is null) return;
        var mine = Repo("mine");
        var other = Repo("other");
        var task = await Task(mine);

        var refused = await Call("graph.load", new { taskId = task, folder = other, scope = "all", @base = (string?)null });
        Assert.False(refused.Ok);
        Assert.Equal("not one of the task's folders", refused.Error);

        var unknown = await Call("graph.load", new { taskId = "T-9999", folder = mine, scope = "all", @base = (string?)null });
        Assert.False(unknown.Ok);

        var loaded = await Call("graph.load", new { taskId = task, folder = mine, scope = "all", @base = (string?)null });
        Assert.True(loaded.Ok);
        Assert.Equal("First line", loaded.Result.GetProperty("rows")[0].GetProperty("subject").GetString());
    }

    [Fact]
    public async Task A_base_branch_must_be_one_git_listed()
    {
        if (_git is null) return;
        var repo = Repo("web");
        Git(repo, "switch", "-q", "-c", "feature");
        var task = await Task(repo);

        var option = await Call("graph.load", new { taskId = task, folder = repo, scope = "branch", @base = "--output=x.txt" });
        Assert.False(option.Ok);
        Assert.False(File.Exists(Path.Combine(repo, "x.txt")));

        var listed = await Call("graph.load", new { taskId = task, folder = repo, scope = "branch", @base = "main" });
        Assert.True(listed.Ok);
        Assert.Equal("main", listed.Result.GetProperty("base").GetString());
    }

    [Fact]
    public async Task Details_are_read_only_for_commits_and_worktrees_the_graph_showed()
    {
        if (_git is null) return;
        var repo = Repo("app");
        var task = await Task(repo);
        File.WriteAllText(Path.Combine(repo, "b.txt"), "new\n");
        var page = (await Call("graph.load", new { taskId = task, folder = repo, scope = "all", @base = (string?)null })).Result;
        var token = page.GetProperty("token").GetString()!;
        var hash = page.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("kind").GetString() == "commit").GetProperty("hash").GetString()!;

        var commit = await Call("graph.commit", new { token, hash });
        Assert.True(commit.Ok);
        Assert.Equal("First line\n\nThe body.", commit.Result.GetProperty("body").GetString());

        foreach (var bad in new[] { "HEAD", "--all", hash[..7], new string('a', 40) })
            Assert.False((await Call("graph.commit", new { token, hash = bad })).Ok);

        var changes = await Call("graph.changes", new { token, path = page.GetProperty("worktrees")[0].GetProperty("path").GetString() });
        Assert.True(changes.Ok);
        Assert.Contains(changes.Result.EnumerateArray(), c => c.GetProperty("path").GetString() == "b.txt" && c.GetProperty("kind").GetString() == "?");
        Assert.False((await Call("graph.changes", new { token, path = _dir })).Ok);

        Assert.False((await Call("graph.commit", new { token = "not-a-token", hash })).Ok);
    }

    [Fact]
    public void A_commit_reads_its_fields_and_whole_message()
    {
        var parsed = GraphApi.ParseCommit("e04a9b3c71f0d2a84e5b19c6d7f203a1b8e46c55\u001fAlex\u001falex@example.com\u001f1764000000\u001faaaaaaa bbbbbbb\u001fMerge feeds\r\n\r\nWith a body.\n\n");
        Assert.NotNull(parsed);
        Assert.Equal(["aaaaaaa", "bbbbbbb"], parsed.Parents);
        Assert.Equal("Merge feeds\n\nWith a body.", parsed.Body);
        Assert.Null(GraphApi.ParseCommit("garbage"));
    }
}
