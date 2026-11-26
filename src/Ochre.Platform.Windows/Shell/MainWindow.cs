using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Ochre.Core.Hosting;
using Ochre.Core.Layout;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows.Shell;

internal sealed class MainWindow : Form, IShellWindow
{
    private readonly IWindowHost _host;
    private readonly ShellOptions _options;
    private readonly IUrlOpener _urls;
    private readonly Microsoft.Web.WebView2.WinForms.WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly SavedWindow? _saved;
    private readonly bool _activateOnShow;
    // Tried in order: where the window was when the layout was saved, then the dev desktop.
    private readonly Guid[] _desktops;
    private WebViewPage? _page;
    private bool _movedToDesktop;
    private volatile string? _task;

    /// <param name="saved">The window as the layout last saved it, when it's being reopened.</param>
    /// <param name="activateOnShow">False for reopened windows: activating one on another desktop would switch to it.</param>
    public MainWindow(
        IWindowHost host,
        ShellOptions options,
        IUrlOpener urls,
        Task<CoreWebView2Environment> environment,
        Icon icon,
        string id,
        string? task,
        SavedWindow? saved,
        bool activateOnShow)
    {
        _host = host;
        _options = options;
        _urls = urls;
        _saved = saved;
        _activateOnShow = activateOnShow;
        _desktops = new[] { saved?.Desktop, options.VirtualDesktop }.OfType<Guid>().Distinct().ToArray();
        Id = id;
        InitialTask = task;
        _task = task;
        Text = options.Title;
        Icon = icon;
        Size = new Size(1400, 900);
        MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;
        if (options.Headless)
        {
            // Still a visible window as far as Windows and WebView2 are concerned, so the page renders and captures.
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            ShowInTaskbar = false;
        }
        else if (saved?.Placement is { } placement && WindowRestore.Place(placement, Screens.All()) is { } bounds)
        {
            // Set before the handle exists, so the window is created on its monitor at that monitor's DPI.
            StartPosition = FormStartPosition.Manual;
            Bounds = Screens.Rect(bounds);
            if (saved.Maximized) WindowState = FormWindowState.Maximized;
        }
        BackColor = Color.FromArgb(0x1b, 0x1a, 0x19);
        _web.DefaultBackgroundColor = BackColor;
        Controls.Add(_web);
        _ = InitAsync(environment);
    }

    public string Id { get; }

    public string? InitialTask { get; }

    /// <summary>The task the page last said it shows.</summary>
    public string? ShownTask => _task;

    /// <summary>The page reported a different task. Raised on whichever thread the bridge runs on.</summary>
    public event Action? TaskChanged;

    /// <summary>Raised as the window starts to close, before it's gone, with why it's closing.</summary>
    public event Action<CloseReason>? ClosingFor;

    public void ReportTask(string? taskId)
    {
        if (_task == taskId) return;
        _task = taskId;
        TaskChanged?.Invoke();
    }

    public void NotifyShown() => _page?.NotifyShown();

    private volatile bool _jumpRequested;

    /// <summary>The jump hotkey chose this window. A page that's loaded is told now; one still loading asks on load.</summary>
    public void ShowJump(bool loaded)
    {
        if (loaded && _page is not null) _page.NotifyJump();
        else _jumpRequested = true;
    }

    public bool TakeJumpRequest()
    {
        var requested = _jumpRequested;
        _jumpRequested = false;
        return requested;
    }

    public CoreWebView2? Core => _page is null ? null : _web.CoreWebView2;

    // A test run's windows, and windows reopened on other desktops, shouldn't take focus.
    protected override bool ShowWithoutActivation => _options.Unattended || !_activateOnShow;

    /// <summary>The window as the layout should save it. UI thread only.</summary>
    public SavedWindow SaveState()
    {
        // When Windows can't say (a window pinned to every desktop), keep the desktop it was reopened on.
        var desktop = VirtualDesktop.DesktopOf(Handle, _options.Log) ?? _saved?.Desktop;
        // An off-screen dev window's place means nothing; keep whatever the layout had.
        if (_options.Headless) return new SavedWindow(_task, desktop, _saved?.Placement, _saved?.Maximized ?? false);

        var normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var screen = Screen.FromRectangle(normal);
        var placement = new SavedPlacement(Screens.Rect(normal), Screens.Rect(screen.Bounds), Screens.Rect(screen.WorkingArea), Screens.ScaleOf(screen.Bounds));
        return new SavedWindow(_task, desktop, placement, WindowState == FormWindowState.Maximized);
    }

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
        _page = new WebViewPage(_web.CoreWebView2, this, _host, _options, _urls, () => !InvokeRequired, a => BeginInvoke(a), ShowPageTitle);
    }

    private void ShowPageTitle(string title)
    {
        // The page's title is untrusted; keep it to one short line.
        var clean = new string(title.Where(c => !char.IsControl(c)).Take(120).ToArray()).Trim();
        Text = clean.Length == 0 || clean == "Ochre" ? _options.Title : $"{clean} - {_options.Title}";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Only the first choice this early: a refusal here may be the not-yet-visible one, not a missing desktop.
        if (_desktops.Length > 0) _movedToDesktop = VirtualDesktop.TryMove(Handle, _desktops[0], _options.Log);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Some Windows builds refuse to move a window that isn't visible yet. A saved desktop that's gone falls back
        // to the dev desktop, if set, or else the window stays on the current one.
        if (_movedToDesktop) return;
        foreach (var desktop in _desktops)
        {
            if (VirtualDesktop.TryMove(Handle, desktop, _options.Log))
            {
                _movedToDesktop = true;
                return;
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel) ClosingFor?.Invoke(e.CloseReason);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _page?.Dispose();
        _page = null;
        base.OnFormClosed(e);
    }
}
