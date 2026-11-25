using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Ochre.Core.Hosting;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows.Shell;

internal sealed class MainWindow : Form
{
    private readonly IWindowHost _host;
    private readonly ShellOptions _options;
    private readonly IUrlOpener _urls;
    private readonly Microsoft.Web.WebView2.WinForms.WebView2 _web = new() { Dock = DockStyle.Fill };
    private WebViewPage? _page;
    private bool _movedToDesktop;

    public MainWindow(IWindowHost host, ShellOptions options, IUrlOpener urls, Task<CoreWebView2Environment> environment, Icon icon)
    {
        _host = host;
        _options = options;
        _urls = urls;
        Text = options.Title;
        Icon = icon;
        Size = new Size(1400, 900);
        MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x1b, 0x1a, 0x19);
        _web.DefaultBackgroundColor = BackColor;
        Controls.Add(_web);
        _ = InitAsync(environment);
    }

    private bool _quitting;

    /// <summary>Closes for real, bypassing hide-on-close, because the app is quitting.</summary>
    public void CloseForQuit()
    {
        _quitting = true;
        Close();
    }

    public void NotifyShown() => _page?.NotifyShown();

    // A window opened onto another desktop for a test run shouldn't steal focus from whatever the user is doing.
    protected override bool ShowWithoutActivation => _options.VirtualDesktop is not null;

    private async Task InitAsync(Task<CoreWebView2Environment> environment)
    {
        try
        {
            var env = await environment.ConfigureAwait(true);
            await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException)
        {
            _options.Log($"WebView2 failed to start: {ex.GetType().Name}: {ex.Message}");
            if (!IsDisposed) Controls.Add(new Label { Text = "WebView2 failed to start. See the log for details.", Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter });
            return;
        }
        if (IsDisposed) return;
        _page = new WebViewPage(_web.CoreWebView2, _host, _options, _urls, () => !InvokeRequired, a => BeginInvoke(a));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MoveToDesktop();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Some Windows builds refuse to move a window that isn't visible yet.
        MoveToDesktop();
    }

    private void MoveToDesktop()
    {
        if (_movedToDesktop || _options.VirtualDesktop is not { } desktop) return;
        _movedToDesktop = VirtualDesktop.TryMove(Handle, desktop, _options.Log);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_options.HideOnClose && !_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _page?.Dispose();
        _page = null;
        base.OnFormClosed(e);
    }
}
