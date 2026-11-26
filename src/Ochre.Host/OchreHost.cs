using Ochre.Core;
using Ochre.Core.Bridge;
using Ochre.Core.Hosting;
using Ochre.Core.Platform;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Ochre.Core.Terminals;
using Ochre.Host.Bridge;

namespace Ochre.Host;

/// <summary>
/// Owns everything stateful (ADR 0008). Windows are views: closing the last one leaves the host and everything
/// it owns running until the user quits from the tray.
/// </summary>
internal sealed class OchreHost : IDisposable
{
    public OchreHost(OchreProfile profile, OchrePaths paths, AppLog log, ITerminalPlatform terminalPlatform, IDataProtector protector, IUrlOpener urls)
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
        Shutdown = new ShutdownParker(Tasks, Sessions, ledger, () => Settings.AutoParkOnShutdown, log);
        Shutdown.ClearAtStart();
        Sessions.Changed += (session, changes) =>
            Windows.Broadcast(BridgeDispatcher.SerializeEvent("sessions.update", SessionApi.Update(session, changes), AppJson.Default.SessionUpdateEvent));
        Sessions.ListChanged += () => Windows.Broadcast(BridgeDispatcher.SerializeEvent("sessions.changed", new Empty(), AppJson.Default.Empty));
        Sessions.StartObserving();
        Parking = new ParkCoordinator(Tasks, Sessions, ParkRequests, log);
        Parking.Changed += id => Windows.Broadcast(BridgeDispatcher.SerializeEvent("tasks.changed", new TaskChangedEvent(id), AppJson.Default.TaskChangedEvent));
        Terminals = new TerminalManager(terminalPlatform, log);
        Terminals.Output += (t, data, seq) =>
            Windows.Broadcast(BridgeDispatcher.SerializeEvent("terminals.output", new TerminalOutputEvent(t.Id, data, seq), AppJson.Default.TerminalOutputEvent));
        Terminals.Changed += () => Windows.Broadcast(BridgeDispatcher.SerializeEvent("terminals.changed", new Empty(), AppJson.Default.Empty));
        TerminalSaver = new TerminalPersistence(Terminals, new TerminalStore(paths.Terminals, protector), log);
        var restored = TerminalSaver.RestoreAll();
        if (restored > 0) log.Info($"restored {restored} terminal tab(s)");
    }

    /// <summary>Saves the terminal tabs and their (encrypted) scrollback so they come back after a quit.</summary>
    public TerminalPersistence TerminalSaver { get; }

    /// <summary>Terminal tabs; their shells keep running with every window closed, until the host quits.</summary>
    public TerminalManager Terminals { get; }

    /// <summary>Opens web links in the user's browser.</summary>
    public IUrlOpener Urls { get; }

    /// <summary>Agents asked for a park note; shared with the agent command handler, which takes the notes.</summary>
    public ParkRequests ParkRequests { get; } = new();
    public ParkCoordinator Parking { get; }

    public OchreSettings Settings { get; }

    /// <summary>Parks tasks when Windows ends the session, if the user turned that on.</summary>
    internal ShutdownParker Shutdown { get; }

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
        // Saved before the shells close, so the scrollback includes everything up to the quit.
        TerminalSaver.SaveNow();
        TerminalSaver.Dispose();
        Terminals.Dispose();
        Sessions.Dispose();
        Tasks.Dispose();
    }
}
