using Microsoft.Web.WebView2.Core;
using Ochre.Core.Hosting;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows.Shell;

/// <summary>Locks a CoreWebView2 down, serves the UI from disk, and connects it to the host.</summary>
internal sealed class WebViewPage : IDisposable
{
    // A reserved name, so the virtual host can never shadow a real site.
    public const string HostName = "app.ochre.example";

    private readonly CoreWebView2 _core;
    private readonly Func<bool> _onUiThread;
    private readonly Action<Action> _postToUi;
    private readonly IWindowSession _session;
    private readonly IUrlOpener _urls;
    private readonly string _allowedOrigin;
    private bool _closed;

    /// <param name="onTitle">The page's title changed (UI thread); untrusted text.</param>
    public WebViewPage(CoreWebView2 core, IShellWindow window, IWindowHost host, ShellOptions options, IUrlOpener urls, Func<bool> onUiThread, Action<Action> postToUi, Action<string> onTitle)
    {
        _core = core;
        _urls = urls;
        _onUiThread = onUiThread;
        _postToUi = postToUi;

        var settings = core.Settings;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
        settings.IsStatusBarEnabled = false;
        settings.AreDevToolsEnabled = options.EnableDevTools;
        settings.AreDefaultContextMenusEnabled = options.EnableDevTools;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        // The page only ever comes from the app's own folder and every other navigation is refused, so a
        // SmartScreen lookup adds nothing; in the M0 spike it stalled each cold page load by ~2 s.
        settings.IsReputationCheckingRequired = false;

        _allowedOrigin = options.DevServer is { } dev ? dev.GetLeftPart(UriPartial.Authority) + "/" : $"https://{HostName}/";

        core.SetVirtualHostNameToFolderMapping(HostName, options.UiFolder, CoreWebView2HostResourceAccessKind.Deny);
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(_allowedOrigin, StringComparison.Ordinal)) return;
            e.Cancel = true;
            OpenInBrowser(e.Uri, newWindow: false);
        };
        core.NewWindowRequested += (_, e) =>
        {
            // Middle click and target=_blank arrive here; both open a new browser window.
            e.Handled = true;
            OpenInBrowser(e.Uri, newWindow: true);
        };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;

        core.DocumentTitleChanged += (_, _) =>
        {
            if (!_closed) onTitle(core.DocumentTitle ?? "");
        };

        _session = host.Connect(window, Post);
        core.WebMessageReceived += OnWebMessage;
        core.Navigate(options.DevServer?.AbsoluteUri ?? $"https://{HostName}/index.html");
    }

    /// <summary>A window brought back into view (restored from minimised, or moved here from another desktop) has no
    /// page load, so tell the page it is visible again.</summary>
    public void NotifyShown() => Post("{\"event\":\"window.shown\",\"payload\":{}}");

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_closed || !e.Source.StartsWith(_allowedOrigin, StringComparison.Ordinal)) return;
        string? message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }
        _ = _session.ReceiveAsync(message);
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

    private void OpenInBrowser(string uri, bool newWindow)
    {
        if (newWindow) _urls.OpenInNewWindow(uri);
        else _urls.Open(uri);
    }

    public void Dispose()
    {
        _closed = true;
        _session.Dispose();
    }
}
