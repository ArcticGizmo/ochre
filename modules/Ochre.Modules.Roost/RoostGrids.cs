using System.Text.Json;
using System.Text.Json.Serialization;
using Ochre.Core;
using Ochre.Core.Claude;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;

namespace Ochre.Modules.Roost;

/// <summary>
/// Every task's grid, kept in the module's <c>roost.json</c> and written atomically on each change. A grid is made
/// the first time a task's grid is asked for, shaped for its sessions, and follows them from then on.
/// </summary>
internal sealed class RoostGrids(string dataFolder, SessionManager sessions, TaskStore tasks, AppLog log)
{
    private readonly Lock _gate = new();
    private readonly string _path = Path.Combine(dataFolder, "roost.json");
    private Dictionary<string, RoostGrid>? _grids;

    /// <summary>The task's sessions in the order they started: what its grid may hold.</summary>
    public IReadOnlyList<string> SessionsOf(string taskId) =>
        sessions.All.Where(s => s.TaskId == taskId).Select(s => s.Id).ToList();

    /// <summary>Runs <paramref name="change"/> on the task's grid (made if it has none, and brought up to date with its
    /// sessions first) and saves it if anything changed. <paramref name="aspect"/> shapes a new grid.</summary>
    public T With<T>(string taskId, double aspect, Func<RoostGrid, IReadOnlyList<string>, T> change)
    {
        var live = SessionsOf(taskId);
        lock (_gate)
        {
            var grids = Load();
            bool dirty = false;
            if (!grids.TryGetValue(taskId, out var grid))
            {
                grids[taskId] = grid = RoostGrid.For(live, aspect);
                dirty = true;
            }
            else if (grid.Sync(live))
            {
                dirty = true;
            }
            var before = Signature(grid);
            var result = change(grid, live);
            if (dirty || Signature(grid) != before) Save(grids);
            return result;
        }
    }

    private static string Signature(RoostGrid g) =>
        $"{g.Layout.Signature}|{string.Join(",", g.Cells.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"))}|{g.Zoomed}";

    // Caller holds _gate.
    private Dictionary<string, RoostGrid> Load()
    {
        if (_grids is not null) return _grids;
        _grids = new Dictionary<string, RoostGrid>(StringComparer.Ordinal);
        if (AtomicFile.ReadShared(_path) is not { } text) return _grids;
        try
        {
            var file = JsonSerializer.Deserialize(text, RoostStoreJson.Default.RoostFile);
            foreach (var (taskId, g) in file?.Grids ?? [])
            {
                // A task deleted since takes its grid with it.
                if (!TaskStore.IsTaskId(taskId) || tasks.Get(taskId) is null) continue;
                _grids[taskId] = RoostGrid.FromState(new RoostGridState(g.Regions, g.Cells, g.Zoomed, g.Known));
            }
        }
        catch (JsonException ex)
        {
            // Unreadable (hand-edited, or from a newer build): start again rather than lose the module.
            log.Warn($"roost.json could not be read, starting with no grids: {ex.Message}");
        }
        return _grids;
    }

    // Caller holds _gate.
    private void Save(Dictionary<string, RoostGrid> grids)
    {
        var file = new RoostFile(grids.ToDictionary(g => g.Key, g =>
        {
            var s = g.Value.ToState();
            return new RoostGridFile([.. s.Regions], new Dictionary<int, string>(s.Cells), s.Zoomed, [.. s.Known]);
        }, StringComparer.Ordinal));
        try
        {
            AtomicFile.Write(_path, JsonSerializer.Serialize(file, RoostStoreJson.Default.RoostFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error("could not save roost.json", ex);
        }
    }
}

internal sealed record RoostFile(Dictionary<string, RoostGridFile> Grids);

internal sealed record RoostGridFile(List<RoostRegion> Regions, Dictionary<int, string> Cells, int? Zoomed, List<string> Known);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(RoostFile))]
internal sealed partial class RoostStoreJson : JsonSerializerContext;
