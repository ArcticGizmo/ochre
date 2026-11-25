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
    /// sign-in); <c>ochre --close-window</c> closes the running instance's window. Debug builds also accept
    /// <c>--dev-eval &lt;js&gt;</c> and <c>--dev-capture &lt;png&gt;</c>, sent to the running instance.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var profile = OchreProfile.Current;
        var (command, argument) = ParseCommand(args);

        using var instance = InstanceChannel.Claim(profile);
        if (instance is null)
        {
            if (command == "tray") return 0;
            var reply = InstanceChannel.Send(profile, command, argument, TimeSpan.FromSeconds(30));
            if (reply is null)
            {
                Console.Error.WriteLine("Ochre is running but did not answer.");
                return 1;
            }
            if (command.StartsWith("dev-", StringComparison.Ordinal)) Console.WriteLine(reply);
            return reply.StartsWith("error:", StringComparison.Ordinal) ? 1 : 0;
        }
        if (command.StartsWith("dev-", StringComparison.Ordinal) || command == "close")
        {
            Console.Error.WriteLine("Ochre is not running.");
            return 1;
        }

        var paths = new OchrePaths(profile);
        using var log = new AppLog(paths.Logs);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("unhandled exception", e.ExceptionObject as Exception);
        log.Info($"starting {AppInfoProvider.Version} profile={(profile.IsDefault ? "default" : profile.Name)}");

        using var host = new OchreHost(profile, paths, log);
        var shell = new WindowsShell(host.Windows, ShellOptionsFor(profile, paths, log), new UrlOpener());
        host.Attach(shell);
        instance.Listen((cmd, arg) => HandleCommand(shell, cmd, arg));

        shell.Run(openWindow: command == "open");
        log.Info("stopped");
        return 0;
    }

    private static (string Command, string? Argument) ParseCommand(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tray":
                    return ("tray", null);
                case "--close-window":
                    return ("close", null);
#if DEBUG
                case "--dev-eval" when i + 1 < args.Length:
                    return ("dev-eval", args[i + 1]);
                case "--dev-capture" when i + 1 < args.Length:
                    return ("dev-capture", Path.GetFullPath(args[i + 1]));
#endif
            }
        }
        return ("open", null);
    }

    private static async Task<string> HandleCommand(WindowsShell shell, string command, string? argument)
    {
        switch (command)
        {
            case "open":
                shell.OpenWindow();
                return "ok";
            case "close":
                shell.CloseWindow();
                return "ok";
#if DEBUG
            case "dev-eval" when argument is not null:
                return await ((IShellDevTools)shell).EvalAsync(argument).ConfigureAwait(false);
            case "dev-capture" when argument is not null:
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
