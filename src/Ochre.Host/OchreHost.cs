using Ochre.Core;
using Ochre.Core.Attention;
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
        Attention = new AttentionBoard(Sessions);
        AttentionActions = new AttentionActions(Attention, Sessions);
        Attention.Changed += () =>
        {
            var snapshot = AttentionApi.Snapshot(Attention, Sessions);
            Windows.Broadcast(BridgeDispatcher.SerializeEvent("attention.changed", snapshot, AppJson.Default.AttentionSnapshot));
            Shell?.SetTrayStatus(TrayStatus(snapshot.NeedsYou));
        };
        Sessions.StartObserving();
        Parking = new ParkCoordinator(Tasks, Sessions, ParkRequests, log);
        Parking.Changed += id => Windows.Broadcast(BridgeDispatcher.SerializeEvent("tasks.changed", new TaskChangedEvent(id), AppJson.Default.TaskChangedEvent));

        Modules = new ModuleHost(
            ModuleCatalog.Create(),
            Settings.ModuleEnabled,
            new ModuleKernel(log, paths.LocalRoot, Tasks, Sessions, ledger, services, Windows.Broadcast,
                new AttentionKernel(Attention, AttentionViews, AttentionActions), ShowTask));
        Modules.StartAll();

        Updates = new AppUpdater(log);
        // Only an installed copy has a path that stays put, so only it is offered a sign-in start.
        SignInStart = Updates.State.State != UpdateStateDto.Unsupported && services.TryGetValue(typeof(ISignInStart), out var signIn) ? (ISignInStart)signIn : null;
        Updates.Changed += state => Windows.Broadcast(BridgeDispatcher.SerializeEvent("app.updateChanged", state, AppJson.Default.UpdateDto));
        Updates.Start();
    }

    public ModuleHost Modules { get; }

    public AppUpdater Updates { get; }

    /// <summary>Null where it isn't offered (not an installed copy).</summary>
    public ISignInStart? SignInStart { get; }

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
    public AttentionBoard Attention { get; }
    public AttentionViews AttentionViews { get; } = new();
    public AttentionActions AttentionActions { get; }
    public IShell? Shell { get; private set; }

    /// <summary>Set when the user asked to restart (to apply module changes); Program starts a new host after this one exits.</summary>
    public bool RestartRequested { get; private set; }

    public void Attach(IShell shell)
    {
        Shell = shell;
        shell.SetTrayStatus(TrayStatus(Attention.Items.Count(i => i.NeedsYou)));
    }

    // A notification was clicked: show its task where the user is, bringing a window from another desktop if one shows it.
    private void ShowTask(string? taskId)
    {
        if (Shell is not { } shell) return;
        if (taskId is null || Tasks.Get(taskId) is null)
        {
            shell.OpenWindow();
            return;
        }
        _ = shell.OpenTaskAsync(taskId, OpenTaskChoice.Bring, null);
    }

    private static string? TrayStatus(int needsYou) => needsYou switch
    {
        0 => null,
        1 => "1 needs you",
        _ => $"{needsYou} need you",
    };

    /// <summary>False for a dev run driven by a test (another desktop, or headless): it mustn't take the user's
    /// system-wide shortcut.</summary>
    public bool RegistersHotkeys { get; set; } = true;

    /// <summary>Why the jump shortcut isn't working (another app has it, or settings.json holds one Ochre can't read), or null.</summary>
    public string? JumpHotkeyError { get; private set; }

    /// <summary>Registers the jump shortcut from settings, replacing the one before.</summary>
    public async Task ApplyJumpHotkeyAsync()
    {
        if (!RegistersHotkeys)
        {
            JumpHotkeyError = "Not registered in a dev run on another desktop or headless; use cli dev jump.";
            return;
        }
        if (Shell is not { } shell) return;
        var text = Settings.JumpHotkey.Trim();
        Hotkey? hotkey = null;
        string? error = null;
        if (text.Length > 0) hotkey = Hotkey.Parse(text, out error);
        if (error is not null) Log.Info($"jump hotkey in settings.json: {error}");
        try
        {
            JumpHotkeyError = error ?? await shell.SetJumpHotkeyAsync(hotkey).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            JumpHotkeyError = ex.Message;
        }
    }

    public void RequestRestart()
    {
        RestartRequested = true;
        Shell?.Quit();
    }

    public void Dispose()
    {
        Updates.Dispose();
        Modules.Dispose();
        Attention.Dispose();
        Sessions.Dispose();
        Tasks.Dispose();
    }
}
