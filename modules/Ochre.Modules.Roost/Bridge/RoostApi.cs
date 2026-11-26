using Ochre.Core.Bridge;
using Ochre.Core.Tasks;

namespace Ochre.Modules.Roost.Bridge;

/// <summary>
/// Grid messages from the UI. The renderer is untrusted, so a layout only ever comes from a preset the host knows, a
/// region must be one of the grid's, and a session must be one of the task's (ADR 0013).
/// </summary>
internal static class RoostApi
{
    private const double DefaultAspect = 1.6;

    public static void Register(BridgeDispatcher bridge, RoostGrids grids, TaskStore tasks, Action<string> changed)
    {
        var j = RoostJson.Default;

        bridge.Handle("roost.get", j.RoostGetRequest, j.RoostGridDto, r =>
        {
            CheckTask(r.TaskId, tasks);
            var aspect = double.IsFinite(r.Aspect) && r.Aspect is > 0.2 and < 6 ? r.Aspect : DefaultAspect;
            return grids.With(r.TaskId, aspect, (g, live) => Dto(r.TaskId, g, live));
        });

        bridge.Handle("roost.applyPreset", j.RoostPresetRequest, j.RoostGridDto, r =>
        {
            CheckTask(r.TaskId, tasks);
            var preset = RoostGridLayout.Presets.FirstOrDefault(p => p.Template.ToString() == r.Preset);
            if (preset.Layout is null) throw new BridgeRejectException("unknown layout");
            return Change(r.TaskId, (g, _) => g.ApplyLayout(preset.Layout));
        });

        bridge.Handle("roost.assign", j.RoostAssignRequest, j.RoostGridDto, r =>
        {
            CheckTask(r.TaskId, tasks);
            return Change(r.TaskId, (g, live) =>
            {
                if (g.Layout.Find(r.RegionId) is null) throw new BridgeRejectException("no such region");
                if (!live.Contains(r.SessionId)) throw new BridgeRejectException("that agent isn't on this task");
                g.Assign(r.RegionId, r.SessionId);
            });
        });

        bridge.Handle("roost.unassign", j.RoostSessionRequest, j.RoostGridDto, r =>
        {
            CheckTask(r.TaskId, tasks);
            return Change(r.TaskId, (g, _) => g.Unassign(r.SessionId));
        });

        bridge.Handle("roost.zoom", j.RoostZoomRequest, j.RoostGridDto, r =>
        {
            CheckTask(r.TaskId, tasks);
            return Change(r.TaskId, (g, _) =>
            {
                if (r.RegionId is { } id && g.Layout.Find(id) is null) throw new BridgeRejectException("no such region");
                g.ToggleZoom(r.RegionId);
            });
        });

        RoostGridDto Change(string taskId, Action<RoostGrid, IReadOnlyList<string>> change)
        {
            var dto = grids.With(taskId, DefaultAspect, (g, live) =>
            {
                change(g, live);
                return Dto(taskId, g, live);
            });
            changed(taskId);
            return dto;
        }
    }

    private static void CheckTask(string taskId, TaskStore tasks)
    {
        if (!TaskStore.IsTaskId(taskId) || tasks.Get(taskId) is null) throw new BridgeRejectException("no such task");
    }

    private static RoostGridDto Dto(string taskId, RoostGrid g, IReadOnlyList<string> live)
    {
        var placed = g.Cells.Values.ToHashSet(StringComparer.Ordinal);
        var geometry = g.Layout.GeometryKey;
        return new RoostGridDto(
            taskId,
            RoostGridLayout.Units,
            Regions(g.Layout, g.At),
            g.Zoomed,
            live.Where(s => !placed.Contains(s)).ToList(),
            RoostGridLayout.Presets.Where(p => p.Layout.GeometryKey == geometry).Select(p => p.Template.ToString()).FirstOrDefault(),
            Presets);
    }

    private static IReadOnlyList<RoostRegionDto> Regions(RoostGridLayout layout, Func<int, string?> at) =>
        layout.ReadingOrder.Select((r, i) => new RoostRegionDto(r.Id, i + 1, r.Row, r.Column, r.RowSpan, r.ColumnSpan, at(r.Id))).ToList();

    private static readonly IReadOnlyList<RoostPresetDto> Presets =
        RoostGridLayout.Presets.Select(p => new RoostPresetDto(p.Template.ToString(), p.Name, Regions(p.Layout, _ => null))).ToList();
}
