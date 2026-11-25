using Spike.Core;

namespace Shell.WebView2;

internal static class Program
{
    private const string AppName = "ochre-spike-webview2";

    [STAThread]
    private static int Main()
    {
        var pipeName = SingleInstance.PipeName(AppName);
        using var mutex = new Mutex(true, $@"Local\{AppName}", out var first);
        if (!first)
        {
            SingleInstance.SignalOpen(pipeName);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using var host = new SpikeHost("webview2");
        using var context = new TrayContext(host);
        using var cts = new CancellationTokenSource();
        SingleInstance.Listen(pipeName, context.OpenWindowFromAnyThread, cts.Token);
        Application.Run(context);
        cts.Cancel();
        return 0;
    }
}
