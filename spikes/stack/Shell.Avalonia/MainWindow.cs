using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using Spike.Core;
using Spike.Shared;

namespace Shell.Avalonia;

internal sealed class MainWindow : Window
{
    private readonly SpikeHost _host;
    private readonly bool _hideOnClose;
    private WebView2Page? _page;

    public MainWindow(SpikeHost host, bool hideOnClose)
    {
        _host = host;
        _hideOnClose = hideOnClose;
        Title = "Ochre spike (Avalonia)";
        Width = 1400;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var web = new NativeWebView();
        web.EnvironmentRequested += (_, e) =>
        {
            e.EnableDevTools = true;
            if (e is WindowsWebView2EnvironmentRequestedEventArgs w)
            {
                w.UserDataFolder = Path.Combine(SpikeHost.DataDir, "webview2-avalonia");
                if (VirtualDesktop.BrowserArguments is { } args) w.AdditionalBrowserArguments = args;
            }
        };
        web.AdapterCreated += (_, e) =>
        {
            // Avalonia exposes no message-posting or virtual-host API, so take the underlying CoreWebView2.
            if (e.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle handle) return;
            var core = CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2);
            _page = WebView2Page.Attach(core, _host, Dispatcher.UIThread.CheckAccess, a => Dispatcher.UIThread.Post(a));
        };
        Content = web;

        // Off-desktop bench runs shouldn't steal focus from whatever the user is doing.
        ShowActivated = VirtualDesktop.Configured is null;
        _moved = VirtualDesktop.TryMoveToConfigured(TryGetPlatformHandle()?.Handle ?? 0, _host.Log);
    }

    private bool _moved;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Some Windows builds refuse to move a window that isn't visible yet.
        if (!_moved && VirtualDesktop.Configured is not null)
            _moved = VirtualDesktop.TryMoveToConfigured(TryGetPlatformHandle()?.Handle ?? 0, _host.Log);
    }

    public bool AllowClose { get; set; }

    public void ReportReadyIfLoaded() => _page?.NotifyShown();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_hideOnClose && !AllowClose && !e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _page?.Close();
        base.OnClosed(e);
    }
}
