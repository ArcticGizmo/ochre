using System.Text.Json;
using Ochre.Core;
using Ochre.Core.Bridge;
using Ochre.Core.Claude;
using Ochre.Core.Tasks;
using Ochre.Modules.Editor;
using Ochre.Modules.Editor.Bridge;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// The editor's bridge (ADR 0015): files only in a task's own folders, saves that never overwrite an unseen change,
/// and git asked for a file's HEAD text only when git listed that file as changed. Runs against a real repo.
/// </summary>
public sealed class EditorApiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ochre-editor-").FullName;
    private readonly string? _git = ExecutableResolver.Find("git");
    private readonly AppLog _log;
    private readonly TaskStore _store;
    private readonly FileWatches _watches;

    public EditorApiTests()
    {
        _log = new AppLog(Path.Combine(_dir, "logs"));
        _store = new TaskStore(Path.Combine(_dir, "tasks"), _log);
        _watches = new FileWatches((_, _) => { }, _log);
    }

    public void Dispose()
    {
        _watches.Dispose();
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
        Directory.CreateDirectory(Path.Combine(repo, "app"));
        File.WriteAllText(Path.Combine(repo, "a.txt"), "hello\n");
        File.WriteAllText(Path.Combine(repo, "app", "b.txt"), "one\ntwo\n");
        Git(repo, "add", "-A");
        Git(repo, "commit", "-q", "-m", "First");
        return repo;
    }

    private async Task<string> Task(params string[] repos) =>
        await _store.CreateAsync("editor test", "user", d => d.SetRepos(repos));

    private async Task<(bool Ok, JsonElement Result, string? Error)> Call(string type, object payload)
    {
        string? reply = null;
        var bridge = new BridgeDispatcher(m => reply = m);
        EditorApi.Register(bridge, _store, new ChangesCache(), _watches);
        await bridge.DispatchAsync(JsonSerializer.Serialize(new { id = 1, type, payload }));
        var root = JsonDocument.Parse(reply!).RootElement.Clone();
        var ok = root.GetProperty("ok").GetBoolean();
        return (ok, ok ? root.GetProperty("result") : default, ok ? null : root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Files_are_read_and_written_only_in_the_tasks_own_folders()
    {
        if (_git is null) return;
        var mine = Repo("mine");
        var other = Repo("other");
        var task = await Task(mine);

        var refused = await Call("editor.read", new { taskId = task, folder = other, path = "a.txt" });
        Assert.False(refused.Ok);
        Assert.Equal("not one of the task's folders", refused.Error);

        var escape = await Call("editor.read", new { taskId = task, folder = mine, path = "../other/a.txt" });
        Assert.False(escape.Ok);

        var hook = await Call("editor.write", new { taskId = task, folder = mine, path = ".git/hooks/pre-commit", text = "x", version = "" });
        Assert.False(hook.Ok);
        Assert.False(File.Exists(Path.Combine(mine, ".git", "hooks", "pre-commit")));

        var read = await Call("editor.read", new { taskId = task, folder = mine, path = "a.txt" });
        Assert.True(read.Ok);
        Assert.Equal("hello\n", read.Result.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_save_names_the_version_it_edited()
    {
        if (_git is null) return;
        var repo = Repo("web");
        var task = await Task(repo);
        var version = (await Call("editor.read", new { taskId = task, folder = repo, path = "a.txt" })).Result.GetProperty("version").GetString();

        var saved = await Call("editor.write", new { taskId = task, folder = repo, path = "a.txt", text = "mine\n", version });
        Assert.True(saved.Result.GetProperty("saved").GetBoolean());

        // The same version again is now stale: the first save moved the file on.
        var stale = await Call("editor.write", new { taskId = task, folder = repo, path = "a.txt", text = "again\n", version });
        Assert.False(stale.Result.GetProperty("saved").GetBoolean());
        Assert.Equal("mine\n", File.ReadAllText(Path.Combine(repo, "a.txt")));
    }

    [Fact]
    public async Task Head_text_is_read_only_for_files_git_lists_as_changed()
    {
        if (_git is null) return;
        var repo = Repo("api");
        var task = await Task(repo);
        File.WriteAllText(Path.Combine(repo, "app", "b.txt"), "one\n2\nthree\n");
        File.WriteAllText(Path.Combine(repo, "new.txt"), "x\ny\n");

        var head = await Call("editor.head", new { taskId = task, folder = repo, path = "app/b.txt" });
        Assert.True(head.Ok);
        Assert.Equal("one\ntwo\n", head.Result.GetProperty("text").GetString());

        var untracked = await Call("editor.head", new { taskId = task, folder = repo, path = "new.txt" });
        Assert.Equal("", untracked.Result.GetProperty("text").GetString());

        var unchanged = await Call("editor.head", new { taskId = task, folder = repo, path = "a.txt" });
        Assert.False(unchanged.Ok);

        var changes = (await Call("editor.changes", new { taskId = task })).Result[0].GetProperty("changes").EnumerateArray().ToList();
        var b = changes.Single(c => c.GetProperty("path").GetString() == "app/b.txt");
        Assert.Equal("M", b.GetProperty("status").GetString());
        Assert.Equal(2, b.GetProperty("added").GetInt32());
        Assert.Equal(1, b.GetProperty("removed").GetInt32());
        var n = changes.Single(c => c.GetProperty("path").GetString() == "new.txt");
        Assert.Equal("?", n.GetProperty("status").GetString());
        Assert.Equal(2, n.GetProperty("added").GetInt32());
    }

    [Fact]
    public async Task A_folder_inside_a_repo_sees_only_its_own_changes_relative_to_it()
    {
        if (_git is null) return;
        var repo = Repo("mono");
        var app = Path.Combine(repo, "app");
        var task = await Task(app);
        File.WriteAllText(Path.Combine(repo, "a.txt"), "outside\n");
        File.WriteAllText(Path.Combine(app, "b.txt"), "inside\n");

        var changes = (await Call("editor.changes", new { taskId = task })).Result[0].GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(["b.txt"], changes.Select(c => c.GetProperty("path").GetString()));

        var head = await Call("editor.head", new { taskId = task, folder = app, path = "b.txt" });
        Assert.Equal("one\ntwo\n", head.Result.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Locate_maps_an_agents_absolute_path_to_a_task_folder()
    {
        if (_git is null) return;
        var repo = Repo("loc");
        var task = await Task(repo);

        var found = await Call("editor.locate", new { taskId = task, path = Path.Combine(repo, "app", "b.txt") });
        Assert.Equal(repo, found.Result.GetProperty("folder").GetString());
        Assert.Equal("app/b.txt", found.Result.GetProperty("path").GetString());

        var outside = await Call("editor.locate", new { taskId = task, path = Path.Combine(_dir, "elsewhere.txt") });
        Assert.Equal(JsonValueKind.Null, outside.Result.GetProperty("folder").ValueKind);
    }
}
