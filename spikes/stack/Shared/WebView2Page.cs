using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Spike.Core;
using Spike.Core.Bridge;

namespace Spike.Shared;

/// <summary>
/// Everything a WebView2-based shell does to a CoreWebView2: lock it down, serve the UI from disk, and wire the
/// bridge. Shared so the shells differ only in tray and window code, which is what the spike compares.
/// </summary>
internal sealed class WebView2Page
{
    public const string HostName = "ochre.spike";

    private readonly CoreWebView2 _core;
    private readonly SpikeHost _host;
    private readonly Func<bool> _onUiThread;
    private readonly Action<Action> _postToUi;
    private readonly BridgeDispatcher _bridge;
    private readonly string _allowedOrigin;
    private bool _closed;

    private WebView2Page(CoreWebView2 core, SpikeHost host, Func<bool> onUiThread, Action<Action> postToUi)
    {
        _core = core;
        _host = host;
        _onUiThread = onUiThread;
        _postToUi = postToUi;

        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = true;

        var devUrl = Environment.GetEnvironmentVariable("OCHRE_SPIKE_DEV_URL");
        _allowedOrigin = devUrl is null ? $"https://{HostName}/" : new Uri(devUrl).GetLeftPart(UriPartial.Authority) + "/";

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

        _bridge = host.AttachWindow(Post);
        core.WebMessageReceived += OnWebMessage;

        var query = Environment.GetEnvironmentVariable("OCHRE_SPIKE_AUTOBENCH") == "1" ? "?autobench=1" : "";
        core.Navigate((devUrl ?? $"https://{HostName}/index.html") + query);
    }

    public static WebView2Page Attach(CoreWebView2 core, SpikeHost host, Func<bool> onUiThread, Action<Action> postToUi) =>
        new(core, host, onUiThread, postToUi);

    /// <summary>A hidden-then-shown window has no page load, so ask the page to report ready again.</summary>
    public void NotifyShown() => Post("{\"event\":\"window.shown\",\"payload\":{}}");

    public void Close()
    {
        _closed = true;
        _host.DetachWindow(_bridge);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(_allowedOrigin, StringComparison.Ordinal)) return;
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
        if (_closed) return;
        if (_onUiThread())
        {
            _core.PostWebMessageAsString(message);
            return;
        }
        try
        {
            _postToUi(() =>
            {
                if (!_closed) _core.PostWebMessageAsString(message);
            });
        }
        catch (InvalidOperationException)
        {
            // Window torn down between the check and the post.
        }
    }

    private static void OpenExternal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)) return;
        Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }
}
