using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Spike.Core;
using Spike.Core.Bridge;

namespace Shell.WebView2;

internal sealed class MainForm : Form
{
    private const string HostName = "ochre.spike";

    private readonly SpikeHost _host;
    private readonly bool _hideOnClose;
    private readonly Microsoft.Web.WebView2.WinForms.WebView2 _web = new() { Dock = DockStyle.Fill };
    private BridgeDispatcher? _bridge;
    private string _allowedOrigin = $"https://{HostName}/";
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

    /// <summary>A hidden-then-shown window has no page load, so tell the host it is ready straight away.</summary>
    public void ReportReadyIfLoaded() => _web.CoreWebView2?.PostWebMessageAsString("{\"event\":\"window.shown\",\"payload\":{}}");

    private async Task InitAsync(Task<CoreWebView2Environment> environment)
    {
        var env = await environment.ConfigureAwait(true);
        await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
        if (IsDisposed) return;

        var core = _web.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = true;

        var devUrl = Environment.GetEnvironmentVariable("OCHRE_SPIKE_DEV_URL");
        if (devUrl is not null) _allowedOrigin = new Uri(devUrl).GetLeftPart(UriPartial.Authority) + "/";

        core.SetVirtualHostNameToFolderMapping(HostName, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Deny);
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(_allowedOrigin, StringComparison.Ordinal)) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };

        _bridge = _host.AttachWindow(Post);
        core.WebMessageReceived += OnWebMessage;
        core.Navigate(devUrl ?? $"https://{HostName}/index.html");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_bridge is null || !e.Source.StartsWith(_allowedOrigin, StringComparison.Ordinal)) return;
        string? message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }
        _ = _bridge.DispatchAsync(message);
    }

    private void Post(string message)
    {
        if (IsDisposed) return;
        if (!InvokeRequired)
        {
            PostNow(message);
            return;
        }
        try
        {
            BeginInvoke(() => PostNow(message));
        }
        catch (InvalidOperationException)
        {
            // Window closed between the check and the invoke.
        }
    }

    private void PostNow(string message)
    {
        if (!IsDisposed) _web.CoreWebView2?.PostWebMessageAsString(message);
    }

    private static void OpenExternal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)) return;
        Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true })?.Dispose();
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
        if (_bridge is not null) _host.DetachWindow(_bridge);
        base.OnFormClosed(e);
    }
}
