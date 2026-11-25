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

        using var instance = InstanceChannel.Claim(profile);
        if (instance is null)
        {
            if (command != "tray") InstanceChannel.Send(profile, command, null, TimeSpan.FromSeconds(10));
            return 0;
        }
        if (command == "close") return 0;

        var paths = new OchrePaths(profile);
        using var log = new AppLog(paths.Logs);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("unhandled exception", e.ExceptionObject as Exception);
        log.Info($"starting {AppInfoProvider.Version} profile={(profile.IsDefault ? "default" : profile.Name)}");

        using var host = new OchreHost(profile, paths, log);
        var shell = new WindowsShell(host.Windows, ShellOptionsFor(profile, paths, log), new UrlOpener());
        host.Attach(shell);
        var agents = new AgentCommands(host.Tasks, host.ParkRequests);
        instance.Listen((cmd, arg) => cmd == "agent" ? agents.HandleAsync(AgentRequest.Parse(arg)) : HandleCommand(shell, profile, cmd, arg));

        shell.Run(openWindow: command == "open");
        log.Info("stopped");
        return 0;
    }

    private static async Task<string> HandleCommand(WindowsShell shell, OchreProfile profile, string command, string? argument)
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
            DevServer = Uri.TryCreate(Environment.GetEnvironmentVariable("OCHRE_DEV_SERVER"), UriKind.Absolute, out var dev) && dev.IsLoopback ? dev : null,
        };
#endif
        return options;
    }
}
