using Ochre.Core;
using Ochre.Core.Hosting;
using Ochre.Host.Bridge;
using Ochre.Platform.Windows;
using Ochre.Platform.Windows.Shell;

namespace Ochre.Host;

internal static class Program
{
    /// <summary>
    /// <c>ochre</c> opens the window (starting the host if needed); <c>ochre --tray</c> starts in the tray only (for
    /// sign-in); <c>ochre --close-window</c> closes the running instance's window.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var profile = OchreProfile.Current;
        var command = args.Contains("--close-window") ? InstanceCommand.CloseWindow : InstanceCommand.OpenWindow;
        using var instance = InstanceChannel.ClaimOrForward(profile, command);
        if (instance is null) return 0;

        var paths = new OchrePaths(profile);
        using var log = new AppLog(paths.Logs);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("unhandled exception", e.ExceptionObject as Exception);
        log.Info($"starting {AppInfoProvider.Version} profile={(profile.IsDefault ? "default" : profile.Name)}");

        using var host = new OchreHost(profile, paths, log);
        var shell = new WindowsShell(host.Windows, ShellOptionsFor(profile, paths, log), new UrlOpener());
        host.Attach(shell);
        instance.Listen(c =>
        {
            if (c == InstanceCommand.CloseWindow) shell.CloseWindow();
            else shell.OpenWindow();
        });

        shell.Run(openWindow: !args.Contains("--tray") && command == InstanceCommand.OpenWindow);
        log.Info("stopped");
        return 0;
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
