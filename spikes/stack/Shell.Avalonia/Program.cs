using Avalonia;
using Avalonia.Controls;
using Spike.Core;

namespace Shell.Avalonia;

internal static class Program
{
    private const string AppName = "ochre-spike-avalonia";

    [STAThread]
    private static int Main(string[] args)
    {
        var pipeName = SingleInstance.PipeName(AppName);
        using var mutex = new Mutex(true, $@"Local\{AppName}", out var first);
        if (!first)
        {
            SingleInstance.Signal(pipeName, args);
            return 0;
        }

        using var host = new SpikeHost("avalonia");
        using var cts = new CancellationTokenSource();
        var app = AppBuilder.Configure(() => new App(host, pipeName, cts.Token)).UsePlatformDetect();
        var code = app.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        cts.Cancel();
        return code;
    }
}
