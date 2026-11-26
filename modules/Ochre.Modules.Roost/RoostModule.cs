using Ochre.Core.Bridge;
using Ochre.Core.Modules;
using Ochre.Modules.Roost.Bridge;

namespace Ochre.Modules.Roost;

/// <summary>The Roost grid (M5, ADR 0013): a task's agents side by side, one live thread per region.</summary>
internal sealed class RoostModule : IModule
{
    public string Id => "roost";
    public string Name => "Agent grid";
    public string Description => "A task's agents side by side in a grid you lay out: answer and message each one in place, and watch outside sessions read-only.";
    public bool DefaultEnabled => true;

    public IReadOnlyList<RequestContract> Requests { get; } =
    [
        new("roost.get", typeof(RoostGetRequest), typeof(RoostGridDto)),
        new("roost.applyPreset", typeof(RoostPresetRequest), typeof(RoostGridDto)),
        new("roost.assign", typeof(RoostAssignRequest), typeof(RoostGridDto)),
        new("roost.unassign", typeof(RoostSessionRequest), typeof(RoostGridDto)),
        new("roost.zoom", typeof(RoostZoomRequest), typeof(RoostGridDto)),
    ];

    public IReadOnlyList<EventContract> Events { get; } =
    [
        // A task's grid changed in some window: fetch it again with roost.get.
        new("roost.changed", typeof(RoostChangedEvent)),
    ];

    public IDisposable Start(IModuleContext context)
    {
        var grids = new RoostGrids(context.DataFolder, context.Sessions, context.Tasks, context.Log);
        context.AddBridge(bridge => RoostApi.Register(bridge, grids, context.Tasks,
            taskId => context.Broadcast("roost.changed", new RoostChangedEvent(taskId), RoostJson.Default.RoostChangedEvent)));
        return new Nothing();
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}
