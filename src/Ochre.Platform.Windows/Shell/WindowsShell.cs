using Microsoft.Web.WebView2.Core;
using Ochre.Core.Hosting;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows.Shell;

/// <summary>Tray icon plus one WebView2 window, on a WinForms message loop. Draws no UI of its own.</summary>
public sealed class WindowsShell : IShell
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

    private sealed class TrayContext : ApplicationContext
    {
        private readonly IWindowHost _host;
        private readonly ShellOptions _options;
        private readonly IUrlOpener _urls;
        private readonly NotifyIcon _tray;
        private readonly Control _invoker = new();
        private readonly Icon _icon = AppIcon.Create();
        private Task<CoreWebView2Environment>? _environment;
        private MainWindow? _window;

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
            if (_window is { IsDisposed: false })
            {
                var wasHidden = !_window.Visible;
                _window.Show();
                if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
                if (_options.VirtualDesktop is null) _window.Activate();
                if (wasHidden) _window.NotifyShown();
                return;
            }

            _environment ??= CoreWebView2Environment.CreateAsync(
                null,
                _options.WebViewDataFolder,
                new CoreWebView2EnvironmentOptions(_options.VirtualDesktop is null ? null : VirtualDesktop.UnthrottledBrowserArguments));
            _window = new MainWindow(_host, _options, _urls, _environment, _icon);
            _window.FormClosed += (_, _) => _window = null;
            _window.Show();
        }

        public void CloseWindow()
        {
            if (_window is not { IsDisposed: false }) return;
            if (_options.HideOnClose) _window.Hide();
            else _window.Close();
        }

        public void Quit()
        {
            _tray.Visible = false;
            if (_window is { IsDisposed: false }) _window.CloseForQuit();
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
