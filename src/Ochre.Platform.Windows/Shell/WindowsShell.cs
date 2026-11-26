using Microsoft.Web.WebView2.Core;
using Ochre.Core.Hosting;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows.Shell;

/// <summary>
/// Tray icon plus WebView2 windows, at most one per virtual desktop from the tray, on a WinForms message loop. Draws
/// no UI of its own.
/// </summary>
public sealed class WindowsShell : IShell, IShellDevTools
{
    private readonly IWindowHost _host;
    private readonly ShellOptions _options;
    private readonly IUrlOpener _urls;
    private TrayContext? _context;

    public WindowsShell(IWindowHost host, ShellOptions options, IUrlOpener urls)
    {
        _host = host;
        _options = options;
        _urls = urls;
    }

    public void Run(bool openWindow)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var context = new TrayContext(_host, _options, _urls);
        _context = context;
        if (openWindow) context.OpenWindow();
        Application.Run(context);
        _context = null;
    }

    public void OpenWindow() => _context?.Post(c => c.OpenWindow());

    public void CloseWindow() => _context?.Post(c => c.CloseWindow());

    public void Quit() => _context?.Post(c => c.Quit());

    public Task<string> EvalAsync(string script) =>
        OnUiThread(async core => await core.ExecuteScriptAsync(script).ConfigureAwait(true));

    public Task CaptureAsync(string path) =>
        OnUiThread(async core =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using var file = File.Create(path);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file).ConfigureAwait(true);
            return path;
        });

    private Task<T> OnUiThread<T>(Func<CoreWebView2, Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = _context;
        if (context is null)
        {
            tcs.SetException(new InvalidOperationException("Ochre is shutting down"));
            return tcs.Task;
        }
        context.Post(async c =>
        {
            try
            {
                var core = c.Core ?? throw new InvalidOperationException("no window is open");
                tcs.SetResult(await action(core).ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private sealed class TrayContext : ApplicationContext
    {
        private readonly IWindowHost _host;
        private readonly ShellOptions _options;
        private readonly IUrlOpener _urls;
        private readonly NotifyIcon _tray;
        private readonly Control _invoker = new();
        private readonly Icon _icon = AppIcon.Create();
        private Task<CoreWebView2Environment>? _environment;
        // Most recently used last; dev eval and capture act on the last one.
        private readonly List<MainWindow> _windows = [];

        public TrayContext(IWindowHost host, ShellOptions options, IUrlOpener urls)
        {
            _host = host;
            _options = options;
            _urls = urls;
            // A handle on the UI thread lets other threads marshal here before any window exists.
            _ = _invoker.Handle;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Ochre", null, (_, _) => OpenWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, (_, _) => Quit());
            _tray = new NotifyIcon { Icon = _icon, Text = options.Title, ContextMenuStrip = menu, Visible = true };
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) OpenWindow();
            };
            Application.Idle += OnFirstIdle;
        }

        public CoreWebView2? Core => _windows.LastOrDefault(w => !w.IsDisposed)?.Core;

        private void OnFirstIdle(object? sender, EventArgs e)
        {
            Application.Idle -= OnFirstIdle;
            _host.TrayReady();
        }

        public void Post(Action<TrayContext> action)
        {
            try
            {
                _invoker.BeginInvoke(() => action(this));
            }
            catch (InvalidOperationException)
            {
                // Shutting down.
            }
        }

        public void OpenWindow()
        {
            _host.WindowOpenRequested();
            // A window on the desktop the user is on comes forward; one on another desktop is left where it is, and a
            // new window opens here instead (roadmap, "Multiple windows": never switch desktops). The dev setting
            // puts every window on one other desktop, so there the single window is always reused.
            var here = _windows.LastOrDefault(w => !w.IsDisposed && (_options.VirtualDesktop is not null || VirtualDesktop.IsOnCurrentDesktop(w.Handle, _options.Log)));
            if (here is not null)
            {
                var wasHidden = !here.Visible;
                here.Show();
                if (here.WindowState == FormWindowState.Minimized) here.WindowState = FormWindowState.Normal;
                if (_options.VirtualDesktop is null) here.Activate();
                if (wasHidden) here.NotifyShown();
                Touch(here);
                return;
            }

            _environment ??= CoreWebView2Environment.CreateAsync(
                null,
                _options.WebViewDataFolder,
                new CoreWebView2EnvironmentOptions(_options.VirtualDesktop is null ? null : VirtualDesktop.UnthrottledBrowserArguments));
            var window = new MainWindow(_host, _options, _urls, _environment, _icon);
            window.FormClosed += (_, _) => _windows.Remove(window);
            window.Activated += (_, _) => Touch(window);
            _windows.Add(window);
            window.Show();
        }

        private void Touch(MainWindow window)
        {
            _windows.Remove(window);
            _windows.Add(window);
        }

        public void CloseWindow()
        {
            foreach (var window in _windows.Where(w => !w.IsDisposed).ToList())
            {
                if (_options.HideOnClose) window.Hide();
                else window.Close();
            }
        }

        public void Quit()
        {
            _tray.Visible = false;
            foreach (var window in _windows.Where(w => !w.IsDisposed).ToList()) window.CloseForQuit();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tray.Dispose();
                _invoker.Dispose();
                _icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
