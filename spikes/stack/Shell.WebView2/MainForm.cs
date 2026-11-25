using Microsoft.Web.WebView2.Core;
using Spike.Core;
using Spike.Shared;

namespace Shell.WebView2;

internal sealed class MainForm : Form
{
    private readonly SpikeHost _host;
    private readonly bool _hideOnClose;
    private readonly Microsoft.Web.WebView2.WinForms.WebView2 _web = new() { Dock = DockStyle.Fill };
    private WebView2Page? _page;
    private bool _quitting;

    public MainForm(SpikeHost host, Task<CoreWebView2Environment> environment, bool hideOnClose)
    {
        _host = host;
        _hideOnClose = hideOnClose;
        Text = "Ochre spike (WebView2)";
        Size = new Size(1400, 900);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);
        Application.ApplicationExit += (_, _) => _quitting = true;
        _ = InitAsync(environment);
    }

    public void ReportReadyIfLoaded() => _page?.NotifyShown();

    private async Task InitAsync(Task<CoreWebView2Environment> environment)
    {
        var env = await environment.ConfigureAwait(true);
        await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
        if (IsDisposed) return;
        _page = WebView2Page.Attach(_web.CoreWebView2, _host, () => !InvokeRequired, a => BeginInvoke(a));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_hideOnClose && !_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _page?.Close();
        base.OnFormClosed(e);
    }
}
