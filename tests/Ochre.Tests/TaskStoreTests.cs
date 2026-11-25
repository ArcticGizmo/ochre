using Ochre.Core;
using Ochre.Core.Tasks;
using Xunit;

namespace Ochre.Tests;

/// <summary>The store's job is to never lose a write: not an editor's, and not one of two agents racing.</summary>
public sealed class TaskStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ochre-tests", Guid.NewGuid().ToString("N"));
    private readonly AppLog _log;
    private readonly TaskStore _store;

    public TaskStoreTests()
    {
        _log = new AppLog(Path.Combine(_root, "logs"));
        _store = new TaskStore(Path.Combine(_root, "tasks"), _log);
        _store.Start();
    }

    [Fact]
    public async Task Ids_are_allocated_in_sequence()
    {
        Assert.Equal("T-0001", await _store.CreateAsync("One", "user"));
        Assert.Equal("T-0002", await _store.CreateAsync("Two", "user"));
        Assert.Equal(["T-0001", "T-0002"], _store.List().Select(t => t.Id));
    }

    [Fact]
    public async Task Deleting_moves_the_file_to_trash_and_never_reuses_its_id()
    {
        await _store.CreateAsync("One", "user");
        var second = await _store.CreateAsync("Two", "user");

        await _store.DeleteAsync(second, "user");

        Assert.False(File.Exists(Path.Combine(_root, "tasks", second + ".md")));
        Assert.True(File.Exists(Path.Combine(_store.TrashFolder, second + ".md")));
        Assert.Equal(["T-0001"], _store.List().Select(t => t.Id));
        Assert.Equal("T-0003", await _store.CreateAsync("Three", "user"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _store.DeleteAsync(second, "user"));
    }

    [Fact]
    public async Task An_edit_made_outside_Ochre_survives_the_next_write()
    {
        var id = await _store.CreateAsync("Task", "user");
        await _store.UpdateAsync(id, "user", d =>
        {
            d.AddItem("First");
            return "added p1";
        });

        // Someone edits the file directly, then an agent writes before the watcher has caught up.
        var path = Path.Combine(_root, "tasks", id + ".md");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("## Notes\n", "## Notes\nTyped in an editor.\n", StringComparison.Ordinal));
        await _store.UpdateAsync(id, "session abc", d => d.SetItemDone("p1", true) ? "checked p1" : null);

        var doc = TaskDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal("Typed in an editor.", doc.Notes);
        Assert.True(doc.FindItem("p1")!.Done);
    }

    [Fact]
    public async Task Concurrent_writers_to_one_task_all_land()
    {
        var id = await _store.CreateAsync("Task", "user");
        await _store.UpdateAsync(id, "user", d =>
        {
            for (var i = 0; i < 20; i++) d.AddItem($"Item {i}");
            return "planned";
        });

        await Task.WhenAll(Enumerable.Range(1, 20).Select(n =>
            Task.Run(() => _store.UpdateAsync(id, $"session {n % 2}", d => d.SetItemDone($"p{n}", true) ? $"checked p{n}" : null))));

        var doc = TaskDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "tasks", id + ".md")));
        Assert.All(doc.AllPlanItems(), item => Assert.True(item.Done));
        Assert.Equal(22, doc.Log.Count);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "tasks"), "*.tmp"));
    }

    public void Dispose()
    {
        _store.Dispose();
        _log.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
