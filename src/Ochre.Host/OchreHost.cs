using Ochre.Core;
using Ochre.Core.Bridge;
using Ochre.Core.Hosting;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Ochre.Host.Bridge;

namespace Ochre.Host;

/// <summary>
/// Owns everything stateful (ADR 0008). Windows are views: closing the last one leaves the host and everything
/// it owns running until the user quits from the tray.
/// </summary>
internal sealed class OchreHost : IDisposable
{
    public OchreHost(OchreProfile profile, OchrePaths paths, AppLog log)
    {
        Profile = profile;
        Paths = paths;
        Log = log;
        Windows = new WindowHost(this);
        Tasks = new TaskStore(paths.Tasks, log);
        Tasks.Changed += id => Windows.Broadcast(BridgeDispatcher.SerializeEvent("tasks.changed", new TaskChangedEvent(id), AppJson.Default.TaskChangedEvent));
        Tasks.Start();
        Sessions = new SessionManager(Tasks, new SessionLedger(paths.SessionLedger), log, profile, new SessionSystem { LocateCli = AgentCli.Locate });
        Sessions.LoadDormant();
        Sessions.Changed += (session, changes) =>
            Windows.Broadcast(BridgeDispatcher.SerializeEvent("sessions.update", SessionApi.Update(session, changes), AppJson.Default.SessionUpdateEvent));
        Sessions.ListChanged += () => Windows.Broadcast(BridgeDispatcher.SerializeEvent("sessions.changed", new Empty(), AppJson.Default.Empty));
        Sessions.StartObserving();
        Parking = new ParkCoordinator(Tasks, Sessions, ParkRequests, log);
        Parking.Changed += id => Windows.Broadcast(BridgeDispatcher.SerializeEvent("tasks.changed", new TaskChangedEvent(id), AppJson.Default.TaskChangedEvent));
    }

    /// <summary>Agents asked for a park note; shared with the agent command handler, which takes the notes.</summary>
    public ParkRequests ParkRequests { get; } = new();
    public ParkCoordinator Parking { get; }

    public OchreProfile Profile { get; }
    public OchrePaths Paths { get; }
    public AppLog Log { get; }
    public WindowHost Windows { get; }
    public TaskStore Tasks { get; }
    public SessionManager Sessions { get; }
    public IShell? Shell { get; private set; }

    public void Attach(IShell shell) => Shell = shell;

    public void Dispose()
    {
        Sessions.Dispose();
        Tasks.Dispose();
    }
}
