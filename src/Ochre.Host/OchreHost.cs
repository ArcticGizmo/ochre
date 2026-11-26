using Ochre.Core;
using Ochre.Core.Bridge;
using Ochre.Core.Hosting;
using Ochre.Core.Modules;
using Ochre.Core.Platform;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Ochre.Host.Bridge;

namespace Ochre.Host;

/// <summary>
/// Owns everything stateful (ADR 0008). Windows are views: closing the last one leaves the host and everything
/// it owns running until the user quits from the tray. The kernel is built here; features beyond it are modules
/// (ADR 0007, 0012).
/// </summary>
internal sealed class OchreHost : IDisposable
{
    /// <param name="services">Platform services modules may ask for, keyed by their Core interface.</param>
    public OchreHost(OchreProfile profile, OchrePaths paths, AppLog log, IUrlOpener urls, IReadOnlyDictionary<Type, object> services)
    {
        Urls = urls;
        Profile = profile;
        Paths = paths;
        Log = log;
        Windows = new WindowHost(this);
        Tasks = new TaskStore(paths.Tasks, log);
        Tasks.Changed += id => Windows.Broadcast(BridgeDispatcher.SerializeEvent("tasks.changed", new TaskChangedEvent(id), AppJson.Default.TaskChangedEvent));
        Tasks.Start();
        Settings = new OchreSettings(paths.Settings);
        var ledger = new SessionLedger(paths.SessionLedger);
        Sessions = new SessionManager(Tasks, ledger, log, profile, new SessionSystem { LocateCli = AgentCli.Locate });
        Sessions.LoadDormant();
        Sessions.Changed += (session, changes) =>
            Windows.Broadcast(BridgeDispatcher.SerializeEvent("sessions.update", SessionApi.Update(session, changes), AppJson.Default.SessionUpdateEvent));
        Sessions.ListChanged += () => Windows.Broadcast(BridgeDispatcher.SerializeEvent("sessions.changed", new Empty(), AppJson.Default.Empty));
        Sessions.StartObserving();
        Parking = new ParkCoordinator(Tasks, Sessions, ParkRequests, log);
        Parking.Changed += id => Windows.Broadcast(BridgeDispatcher.SerializeEvent("tasks.changed", new TaskChangedEvent(id), AppJson.Default.TaskChangedEvent));

        Modules = new ModuleHost(
            ModuleCatalog.Create(),
            Settings.ModuleEnabled,
            new ModuleKernel(log, paths.LocalRoot, Tasks, Sessions, ledger, services, Windows.Broadcast));
        Modules.StartAll();
    }

    public ModuleHost Modules { get; }

    /// <summary>Opens web links in the user's browser.</summary>
    public IUrlOpener Urls { get; }

    /// <summary>Agents asked for a park note; shared with the agent command handler, which takes the notes.</summary>
    public ParkRequests ParkRequests { get; } = new();
    public ParkCoordinator Parking { get; }

    public OchreSettings Settings { get; }

    public OchreProfile Profile { get; }
    public OchrePaths Paths { get; }
    public AppLog Log { get; }
    public WindowHost Windows { get; }
    public TaskStore Tasks { get; }
    public SessionManager Sessions { get; }
    public IShell? Shell { get; private set; }

    /// <summary>Set when the user asked to restart (to apply module changes); Program starts a new host after this one exits.</summary>
    public bool RestartRequested { get; private set; }

    public void Attach(IShell shell) => Shell = shell;

    public void RequestRestart()
    {
        RestartRequested = true;
        Shell?.Quit();
    }

    public void Dispose()
    {
        Modules.Dispose();
        Sessions.Dispose();
        Tasks.Dispose();
    }
}
