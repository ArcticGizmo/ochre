using Ochre.Core.Bridge;
using Ochre.Core.Modules;
using Ochre.Core.Sessions;

namespace Ochre.Modules.ShutdownPark;

/// <summary>Parks tasks with running agents when Windows shuts down or signs out (M2 slice 5); off unless chosen.</summary>
internal sealed class ShutdownParkModule : IModule
{
    public string Id => "shutdown-park";
    public string Name => "Park tasks when Windows shuts down";
    public string Description => "Each task with agents running is parked with an automatic note naming them and the last step ticked. There's no time to ask the agents for notes; they come back dormant and pick up where they were when you send to them. If the shutdown is cancelled, the tasks are put back.";
    public bool DefaultEnabled => false;
    public IReadOnlyList<RequestContract> Requests { get; } = [];
    public IReadOnlyList<EventContract> Events { get; } = [];

    public IDisposable Start(IModuleContext context)
    {
        var parker = new ShutdownParker(context.Tasks, context.Sessions, context.Ledger, () => true, context.Log);
        parker.ClearAtStart();
        context.OnSessionEnding(parker.OnSessionEnding);
        return new Stopper();
    }

    private sealed class Stopper : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
