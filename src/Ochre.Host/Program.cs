using Ochre.Core;
using Ochre.Core.Hosting;
using Ochre.Core.Sessions;
using Ochre.Host.Bridge;
using Ochre.Platform.Windows;
using Ochre.Platform.Windows.Shell;

namespace Ochre.Host;

internal static class Program
{
    /// <summary>
    /// <c>ochre</c> opens a window (starting the host if needed, which reopens the windows saved at the last quit);
    /// <c>ochre --tray</c> starts in the tray only (for sign-in; the saved windows reopen with the first window
    /// opened); <c>ochre --close-window</c> closes the running instance's windows. Everything else goes through the
    /// console CLI (src/Ochre.Cli), which talks to this process over the instance pipe.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var profile = OchreProfile.Current;
        var command = args.Contains("--tray") ? "tray" : args.Contains("--close-window") ? "close" : "open";

        bool restart;
        using (var instance = InstanceChannel.Claim(profile))
        {
            if (instance is null)
            {
                if (command != "tray") InstanceChannel.Send(profile, command, null, TimeSpan.FromSeconds(10));
                return 0;
            }
            if (command == "close") return 0;
            restart = RunHost(profile, instance, openWindow: command == "open");
        }

        // Started only now that this instance's pipe and mutex are released, so the new one can claim them.
        if (restart) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
        return 0;
    }

    /// <summary>Runs the host until the user quits; true when they asked for a restart.</summary>
    private static bool RunHost(OchreProfile profile, InstanceChannel instance, bool openWindow)
    {
        var paths = new OchrePaths(profile);
        using var log = new AppLog(paths.Logs);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("unhandled exception", e.ExceptionObject as Exception);
        log.Info($"starting {AppInfoProvider.Version} profile={(profile.IsDefault ? "default" : profile.Name)}");

        var urls = new UrlOpener();
        var shellOptions = ShellOptionsFor(profile, paths, log);
        using var notifier = NotifierFor(profile, paths, shellOptions, log);
        var services = new Dictionary<Type, object>
        {
            [typeof(Ochre.Core.Platform.ITerminalPlatform)] = new Ochre.Platform.Windows.Terminals.ConPtyPlatform(),
            [typeof(Ochre.Core.Platform.IDataProtector)] = new DataProtector(),
            [typeof(Ochre.Core.Platform.IUrlOpener)] = urls,
            [typeof(Ochre.Core.Platform.IDesktopNotifier)] = notifier,
        };
        using var host = new OchreHost(profile, paths, log, urls, services);
        var shell = new WindowsShell(host.Windows, shellOptions, urls);
        host.Attach(shell);
        host.RegistersHotkeys = !shellOptions.Unattended;
        // Waits inside the shell for the message loop that owns the hotkey.
        _ = host.ApplyJumpHotkeyAsync();
        var agents = new AgentCommands(host.Tasks, host.ParkRequests);
        using var ending = new SessionEnding();
        ending.Ending += () =>
        {
            try
            {
                host.Modules.SessionEnding();
            }
            catch (Exception ex)
            {
                // Never let a failure here get in the way of Windows shutting down.
                log.Error("shutdown handling failed", ex);
            }
        };
        instance.Listen((cmd, arg) => cmd == "agent" ? agents.HandleAsync(AgentRequest.Parse(arg)) : HandleCommand(shell, host, profile, notifier, cmd, arg));

        shell.Run(openWindow);
        log.Info(host.RestartRequested ? "stopped; restarting" : "stopped");
        return host.RestartRequested;
    }

    private static async Task<string> HandleCommand(WindowsShell shell, OchreHost host, OchreProfile profile, Ochre.Core.Platform.IDesktopNotifier notifier, string command, string? argument)
    {
        switch (command)
        {
            case "ping":
                return $"running {AppInfoProvider.Version} profile={(profile.IsDefault ? "default" : profile.Name)} pid={Environment.ProcessId}";
            case "open":
                shell.OpenWindow();
                return "ok";
            case "open-task" when argument is not null:
                return await OpenTask(shell, host, argument).ConfigureAwait(false);
            case "close":
                shell.CloseWindow();
                return "ok";
            case "quit":
                shell.Quit();
                return "ok";
#if DEBUG
            case "dev-eval" when argument is not null:
                var (evalWindow, script) = DevTarget(argument);
                return await ((IShellDevTools)shell).EvalAsync(script, evalWindow).ConfigureAwait(false);
            case "dev-capture" when argument is not null:
                var (captureWindow, path) = DevTarget(argument);
                if (!Path.IsPathFullyQualified(path)) return "error: the capture path must be absolute";
                await ((IShellDevTools)shell).CaptureAsync(path, captureWindow).ConfigureAwait(false);
                return path;
            case "dev-jump":
                ((IShellDevTools)shell).PressJumpHotkey();
                return "ok";
            case "dev-windows":
                return await ((IShellDevTools)shell).ListWindowsAsync().ConfigureAwait(false);
            case "dev-move-window" when argument is not null:
                var (moveWindow, desktop) = DevTarget(argument);
                if (moveWindow is null || !Guid.TryParse(desktop, out var desktopId)) return "error: expected a window id and a desktop guid";
                return await ((IShellDevTools)shell).MoveWindowAsync(moveWindow, desktopId).ConfigureAwait(false);
            case "dev-notifications":
                return notifier is RecordingNotifier recorded ? recorded.Describe() : "error: notifications are real in this run (not headless, or OCHRE_DEV_NOTIFY=1)";
            case "dev-notify" when argument is not null:
                if (notifier is not RecordingNotifier recorder) return "error: notifications are real in this run";
                // tag, action, then key=value inputs, one per line.
                var lines = argument.Split('\n');
                if (lines.Length < 2) return "error: expected a tag and an action";
                var inputs = lines.Skip(2).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
                return recorder.Press(lines[0], lines[1], inputs);
            case "dev-session-ending":
                // What Windows' shutdown request does, without shutting down; the cancel check runs as it would.
                host.Modules.SessionEnding();
                return host.Modules.RunningIds.Contains("shutdown-park")
                    ? $"ok; tasks are put back in {ShutdownParker.CancelledAfter.TotalSeconds:0} s unless Ochre stops first"
                    : "ok; the shutdown-park module is off, so nothing was parked";
#endif
            default:
                await Task.CompletedTask.ConfigureAwait(false);
                return $"error: unknown command '{command}'";
        }
    }

    /// <summary>
    /// <c>ochre-cli open T-0042 [--bring|--new]</c>. The reply is the outcome (<c>opened</c>, <c>focused</c>,
    /// <c>moved</c>), or <c>elsewhere</c> when only a window on another desktop shows the task and the CLI should ask.
    /// </summary>
    private static async Task<string> OpenTask(WindowsShell shell, OchreHost host, string argument)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var id = parts.Length > 0 ? parts[0] : "";
        if (id.Length > 64 || host.Tasks.Get(id) is null) return $"error: no task {id}";
        var choice = parts.Length > 1 && parts[1] == "bring" ? OpenTaskChoice.Bring : parts.Length > 1 && parts[1] == "new" ? OpenTaskChoice.New : OpenTaskChoice.Ask;
        var outcome = await shell.OpenTaskAsync(id, choice, null).ConfigureAwait(false);
        return outcome.ToString().ToLowerInvariant();
    }

#if DEBUG
    /// <summary>A dev command's argument, optionally led by a window id and a newline (the CLI's <c>--window</c>).</summary>
    private static (string? Window, string Value) DevTarget(string argument)
    {
        var newline = argument.IndexOf('\n', StringComparison.Ordinal);
        if (newline > 0 && argument[0] == 'w' && argument[1..newline].All(char.IsAsciiDigit)) return (argument[..newline], argument[(newline + 1)..]);
        return (null, argument);
    }
#endif

    // A headless dev run is driven by tests while the user works, so its notifications are recorded rather than shown
    // unless OCHRE_DEV_NOTIFY=1 asks for real ones. Each profile has its own identity, so dev notifications never look
    // like the installed Ochre's.
    private static Ochre.Core.Platform.IDesktopNotifier NotifierFor(OchreProfile profile, OchrePaths paths, ShellOptions options, AppLog log)
    {
        if (options.Headless && Environment.GetEnvironmentVariable("OCHRE_DEV_NOTIFY") != "1") return new RecordingNotifier(log.Info);
        var aumid = profile.IsDefault ? "Ochre" : $"Ochre.{profile.Name}";
        return new Ochre.Platform.Windows.Notifications.ToastNotifier(aumid, options.Title, Path.Combine(paths.LocalRoot, "notifications"), log.Info);
    }

    private static ShellOptions ShellOptionsFor(OchreProfile profile, OchrePaths paths, AppLog log)
    {
        var options = new ShellOptions(
            profile.IsDefault ? "Ochre" : $"Ochre ({profile.Name})",
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            paths.WebView,
            log.Info)
        {
            WindowLayoutFile = paths.WindowLayout,
            VirtualDesktop = Guid.TryParse(Environment.GetEnvironmentVariable("OCHRE_DEV_DESKTOP"), out var desktop) ? desktop : null,
        };
#if DEBUG
        options = options with
        {
            EnableDevTools = true,
            Headless = Environment.GetEnvironmentVariable("OCHRE_DEV_HEADLESS") == "1",
            DevServer = Uri.TryCreate(Environment.GetEnvironmentVariable("OCHRE_DEV_SERVER"), UriKind.Absolute, out var dev) && dev.IsLoopback ? dev : null,
        };
#endif
        return options;
    }
}
