using Microsoft.Web.WebView2.Core;
using Spike.Core;

namespace Shell.WebView2;

internal sealed class TrayContext : ApplicationContext
{
    private readonly SpikeHost _host;
    private readonly NotifyIcon _tray;
    private readonly Control _invoker = new();
    private readonly bool _hideOnClose = Environment.GetEnvironmentVariable("OCHRE_SPIKE_HIDE_ON_CLOSE") == "1";
    private Task<CoreWebView2Environment>? _environment;
    private MainForm? _window;

    public TrayContext(SpikeHost host)
    {
        _host = host;
        // Forces a handle on the UI thread so pipe callbacks can marshal back to it before any window exists.
        _ = _invoker.Handle;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenWindow());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Ochre spike (WebView2)",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenWindow();
        };

        Application.Idle += OnFirstIdle;
    }

    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        _host.MarkTrayReady();
    }

    public void OpenWindowFromAnyThread()
    {
        try
        {
            _invoker.BeginInvoke(OpenWindow);
        }
        catch (InvalidOperationException)
        {
            // Shutting down.
        }
    }

    public void CloseWindowFromAnyThread()
    {
        try
        {
            _invoker.BeginInvoke(() =>
            {
                if (_window is not { IsDisposed: false }) return;
                if (_hideOnClose) _window.Hide();
                else _window.Close();
            });
        }
        catch (InvalidOperationException)
        {
            // Shutting down.
        }
    }

    private void OpenWindow()
    {
        if (_window is { IsDisposed: false })
        {
            _host.MarkWindowOpenRequested();
            _window.Show();
            if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
            _window.Activate();
            _window.ReportReadyIfLoaded();
            return;
        }

        _host.MarkWindowOpenRequested();
        _environment ??= CoreWebView2Environment.CreateAsync(
            null,
            Path.Combine(SpikeHost.DataDir, "webview2"),
            new CoreWebView2EnvironmentOptions(VirtualDesktop.BrowserArguments));
        _window = new MainForm(_host, _environment, _hideOnClose);
        _window.FormClosed += (_, _) => _window = null;
        _window.Show();
    }

    private void Quit()
    {
        _tray.Visible = false;
        _window?.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _invoker.Dispose();
        }
        base.Dispose(disposing);
    }
}
