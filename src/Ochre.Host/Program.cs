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
    /// <c>ochre</c> opens the window (starting the host if needed); <c>ochre --tray</c> starts in the tray only (for
    /// sign-in); <c>ochre --close-window</c> closes the running instance's window. Everything else goes through the
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
        var services = new Dictionary<Type, object>
        {
            [typeof(Ochre.Core.Platform.ITerminalPlatform)] = new Ochre.Platform.Windows.Terminals.ConPtyPlatform(),
            [typeof(Ochre.Core.Platform.IDataProtector)] = new DataProtector(),
            [typeof(Ochre.Core.Platform.IUrlOpener)] = urls,
        };
        using var host = new OchreHost(profile, paths, log, urls, services);
        var shell = new WindowsShell(host.Windows, ShellOptionsFor(profile, paths, log), urls);
        host.Attach(shell);
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
        instance.Listen((cmd, arg) => cmd == "agent" ? agents.HandleAsync(AgentRequest.Parse(arg)) : HandleCommand(shell, host, profile, cmd, arg));

        shell.Run(openWindow);
        log.Info(host.RestartRequested ? "stopped; restarting" : "stopped");
        return host.RestartRequested;
    }

    private static async Task<string> HandleCommand(WindowsShell shell, OchreHost host, OchreProfile profile, string command, string? argument)
    {
        switch (command)
        {
            case "ping":
                return $"running {AppInfoProvider.Version} profile={(profile.IsDefault ? "default" : profile.Name)} pid={Environment.ProcessId}";
            case "open":
                shell.OpenWindow();
                return "ok";
            case "close":
                shell.CloseWindow();
                return "ok";
            case "quit":
                shell.Quit();
                return "ok";
#if DEBUG
            case "dev-eval" when argument is not null:
                return await ((IShellDevTools)shell).EvalAsync(argument).ConfigureAwait(false);
            case "dev-capture" when argument is not null && Path.IsPathFullyQualified(argument):
                await ((IShellDevTools)shell).CaptureAsync(argument).ConfigureAwait(false);
                return argument;
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

    private static ShellOptions ShellOptionsFor(OchreProfile profile, OchrePaths paths, AppLog log)
    {
        var options = new ShellOptions(
            profile.IsDefault ? "Ochre" : $"Ochre ({profile.Name})",
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            paths.WebView,
            log.Info)
        {
            HideOnClose = Environment.GetEnvironmentVariable("OCHRE_HIDE_ON_CLOSE") == "1",
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
