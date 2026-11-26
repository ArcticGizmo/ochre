using System.Text;
using Microsoft.Web.WebView2.Core;
using Ochre.Core.Hosting;
using Ochre.Core.Layout;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows.Shell;

/// <summary>
/// Tray icon plus WebView2 windows on a WinForms message loop. Any number of windows, each on its own task and
/// virtual desktop; the open windows are saved to <c>windows.json</c> and reopened on their desktops at the next
/// start. Draws no UI of its own.
/// </summary>
public sealed class WindowsShell : IShell, IShellDevTools
{
    private readonly IWindowHost _host;
    private readonly ShellOptions _options;
    private readonly IUrlOpener _urls;
    // The instance pipe listens before Run starts the message loop, so a command can arrive first; it waits for this.
    private readonly TaskCompletionSource<TrayContext> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        _started.TrySetResult(context);
        if (openWindow) context.OpenWindow();
        Application.Run(context);
        _context = null;
    }

    public void OpenWindow() => Post(c => c.OpenWindow());

    public Task<OpenTaskOutcome> OpenTaskAsync(string taskId, OpenTaskChoice choice, IShellWindow? from) =>
        OnUiThread(c => Task.FromResult(c.OpenTask(taskId, choice, from)));

    public void CloseWindow() => Post(c => c.CloseWindow());

    // Registering the hotkey opens no window, so it doesn't reopen the saved ones either.
    public Task<string?> SetJumpHotkeyAsync(Hotkey? hotkey) => OnUiThread(c => Task.FromResult(c.SetJumpHotkey(hotkey)), restore: false);

    public void Quit() => Post(c => c.Quit());

    public void SetTrayStatus(string? status) => Post(c => c.SetTrayStatus(status));

    private void Post(Action<TrayContext> action)
    {
        if (_context is { } context) context.Post(action);
        else if (!_started.Task.IsCompleted) _started.Task.ContinueWith(t => t.Result.Post(action), TaskScheduler.Default);
    }

    public Task<string> EvalAsync(string script, string? windowId = null) =>
        OnUiThread(async c => await c.CoreOf(windowId).ExecuteScriptAsync(script).ConfigureAwait(true));

    public Task CaptureAsync(string path, string? windowId = null) =>
        OnUiThread(async c =>
        {
            var core = c.CoreOf(windowId);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using var file = File.Create(path);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file).ConfigureAwait(true);
            return path;
        });

    public void PressJumpHotkey() => Post(c => c.Jump());

    public Task<string> ListWindowsAsync() => OnUiThread(c => Task.FromResult(c.ListWindows()));

    public Task<string> MoveWindowAsync(string windowId, Guid desktop) => OnUiThread(c => Task.FromResult(c.MoveWindow(windowId, desktop)));

    private async Task<T> OnUiThread<T>(Func<TrayContext, Task<T>> action, bool restore = true)
    {
        try
        {
            await _started.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Ochre's windows haven't started");
        }
        var context = _context ?? throw new InvalidOperationException("Ochre is shutting down");
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async c =>
        {
            try
            {
                // Reopening the saved windows posts work of its own (each window's move to its desktop); run after it.
                if (restore && c.RestoreIfFirst())
                {
                    c.Post(async c2 => await Complete(c2).ConfigureAwait(true));
                    return;
                }
                await Complete(c).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return await tcs.Task.ConfigureAwait(false);

        async Task Complete(TrayContext c)
        {
            try
            {
                tcs.TrySetResult(await action(c).ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }
    }

    private sealed class TrayContext : ApplicationContext
    {
        private readonly IWindowHost _host;
        private readonly ShellOptions _options;
        private readonly IUrlOpener _urls;
        private readonly NotifyIcon _tray;
        private readonly Control _invoker = new();
        private readonly Icon _icon = AppIcon.Create();
        private readonly WindowLayoutStore? _layout;
        private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 1000 };
        private readonly Lock _writeGate = new();
        private Task<CoreWebView2Environment>? _environment;
        private GlobalHotkey? _hotkey;
        // Most recently used last; dev eval and capture act on the last one unless told which.
        private readonly List<MainWindow> _windows = [];
        private int _nextId;
        // The saved layout is read once, by the first window opened, and never written before then: a start in the
        // tray that quits without opening a window keeps the windows saved last time.
        private bool _restored;
        // Set once quitting or Windows shutting down has saved the layout, so windows closing after that aren't
        // dropped from it.
        private bool _frozen;
        private long _saveVersion;
        private long _writtenVersion;

        public TrayContext(IWindowHost host, ShellOptions options, IUrlOpener urls)
        {
            _host = host;
            _options = options;
            _urls = urls;
            _layout = options.WindowLayoutFile is { } file ? new WindowLayoutStore(file) : null;
            // A handle on the UI thread lets other threads marshal here before any window exists.
            _ = _invoker.Handle;
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                Save(wait: false);
            };

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

        public void SetTrayStatus(string? status)
        {
            var text = status is null ? _options.Title : $"{_options.Title}: {status}";
            // Windows refuses a tray tooltip longer than 127 characters.
            _tray.Text = text.Length <= 127 ? text : text[..127];
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

        private IEnumerable<MainWindow> Open => _windows.Where(w => !w.IsDisposed);

        /// <summary>Reopens the saved windows if nothing has yet; true when it did.</summary>
        public bool RestoreIfFirst()
        {
            if (_restored) return false;
            _restored = true;
            if (_layout is null) return false;
            var saved = _layout.Load();
            foreach (var w in saved)
            {
                var task = w.Task is { } t && _host.TaskExists(t) ? t : null;
                Create(task, w, activate: false);
            }
            return saved.Count > 0;
        }

        /// <summary>
        /// A window on the desktop the user is on comes forward; one on another desktop is left where it is, and a
        /// new window opens here instead (roadmap, "Multiple windows": never switch desktops).
        /// </summary>
        public void OpenWindow()
        {
            _host.WindowOpenRequested();
            if (RestoreIfFirst())
            {
                Post(c => c.BringOrOpenHere());
                return;
            }
            BringOrOpenHere();
        }

        private void BringOrOpenHere(bool jump = false)
        {
            if (Open.LastOrDefault(IsHere) is { } here)
            {
                BringForward(here, notify: false);
                if (jump) here.ShowJump(loaded: true);
            }
            else Create(null, null, activate: true, jump: jump);
        }

        public string? SetJumpHotkey(Hotkey? hotkey)
        {
            _hotkey ??= new GlobalHotkey(Jump);
            var error = _hotkey.Set(hotkey);
            if (error is not null) _options.Log($"jump hotkey: {error}");
            return error;
        }

        /// <summary>The jump hotkey: the picker in a window on the desktop the user is on, opening one if needed.</summary>
        public void Jump()
        {
            _host.WindowOpenRequested();
            if (RestoreIfFirst())
            {
                Post(c => c.BringOrOpenHere(jump: true));
                return;
            }
            BringOrOpenHere(jump: true);
        }

        public OpenTaskOutcome OpenTask(string taskId, OpenTaskChoice choice, IShellWindow? from)
        {
            var showing = Open.Where(w => !ReferenceEquals(w, from) && w.ShownTask == taskId).ToList();
            if (showing.LastOrDefault(IsHere) is { } here)
            {
                BringForward(here, notify: false);
                return OpenTaskOutcome.Focused;
            }
            if (showing.LastOrDefault() is { } elsewhere)
            {
                if (choice == OpenTaskChoice.Ask) return OpenTaskOutcome.Elsewhere;
                if (choice == OpenTaskChoice.Bring)
                {
                    if (MoveHere(elsewhere, from as MainWindow))
                    {
                        BringForward(elsewhere, notify: true);
                        return OpenTaskOutcome.Moved;
                    }
                    _options.Log($"couldn't tell which desktop is current; opening {taskId} in a new window instead");
                }
            }
            Create(taskId, null, activate: true);
            return OpenTaskOutcome.Opened;
        }

        /// <summary>
        /// On the desktop the user is looking at. With a dev desktop set, that desktop stands in for it; headless
        /// alone keeps every window "here", so a test run reuses one window as before.
        /// </summary>
        private bool IsHere(MainWindow w)
        {
            if (_options.VirtualDesktop is { } dev) return VirtualDesktop.DesktopOf(w.Handle, _options.Log) == dev;
            return _options.Headless || VirtualDesktop.IsOnCurrentDesktop(w.Handle, _options.Log);
        }

        private bool MoveHere(MainWindow w, MainWindow? from)
        {
            var here = _options.VirtualDesktop
                ?? (from is { IsDisposed: false } ? VirtualDesktop.DesktopOf(from.Handle, _options.Log) : null)
                ?? VirtualDesktop.Current(_options.Log);
            return here is { } d && VirtualDesktop.TryMove(w.Handle, d, _options.Log);
        }

        private void BringForward(MainWindow w, bool notify)
        {
            var wasMinimized = w.WindowState == FormWindowState.Minimized;
            w.Show();
            if (wasMinimized) w.WindowState = FormWindowState.Normal;
            if (!_options.Unattended) w.Activate();
            if (wasMinimized || notify) w.NotifyShown();
            Touch(w);
        }

        private void Create(string? task, SavedWindow? saved, bool activate, bool jump = false)
        {
            _environment ??= CoreWebView2Environment.CreateAsync(
                null,
                _options.WebViewDataFolder,
                new CoreWebView2EnvironmentOptions(_options.Unattended ? VirtualDesktop.UnthrottledBrowserArguments : null));
            var window = new MainWindow(_host, _options, _urls, _environment, _icon, $"w{++_nextId}", task, saved, activate);
            if (jump) window.ShowJump(loaded: false);
            window.ClosingFor += reason =>
            {
                // Windows ending the session closes every window; save them first, as quitting does.
                if (reason != CloseReason.UserClosing) Freeze();
            };
            window.FormClosed += (_, _) =>
            {
                _windows.Remove(window);
                ScheduleSave();
            };
            window.Activated += (_, _) =>
            {
                Touch(window);
                // The user may have moved it to another desktop from Task View, which raises nothing else.
                ScheduleSave();
            };
            window.ResizeEnd += (_, _) => ScheduleSave();
            window.SizeChanged += (_, _) => ScheduleSave();
            window.TaskChanged += () => Post(c => c.ScheduleSave());
            _windows.Add(window);
            window.Show();
            ScheduleSave();
        }

        private void Touch(MainWindow window)
        {
            _windows.Remove(window);
            _windows.Add(window);
        }

        public void CloseWindow()
        {
            foreach (var window in Open.ToList()) window.Close();
        }

        public void Quit()
        {
            Freeze();
            _tray.Visible = false;
            foreach (var window in Open.ToList()) window.Close();
            ExitThread();
        }

        private void Freeze()
        {
            if (_frozen) return;
            _saveTimer.Stop();
            Save(wait: true);
            _frozen = true;
        }

        private void ScheduleSave()
        {
            if (!_restored || _frozen || _layout is null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void Save(bool wait)
        {
            if (!_restored || _frozen || _layout is not { } layout) return;
            var windows = Open.Where(w => w.IsHandleCreated).Select(w => w.SaveState()).ToList();
            var version = ++_saveVersion;
            void Write()
            {
                lock (_writeGate)
                {
                    // Writes run on the pool and may start out of order; never let an older layout replace a newer one.
                    if (version <= _writtenVersion) return;
                    layout.Save(windows);
                    _writtenVersion = version;
                }
            }
            if (wait) Write();
            else _ = Task.Run(Write);
        }

        public CoreWebView2 CoreOf(string? windowId)
        {
            var window = windowId is null ? Open.LastOrDefault() : Open.FirstOrDefault(w => w.Id == windowId);
            if (window is null) throw new InvalidOperationException(windowId is null ? "no window is open" : $"no window {windowId}");
            return window.Core ?? throw new InvalidOperationException($"window {window.Id} hasn't loaded its page yet");
        }

        public string ListWindows()
        {
            var lines = new StringBuilder();
            foreach (var w in Open.OrderBy(w => int.Parse(w.Id[1..], System.Globalization.CultureInfo.InvariantCulture)))
            {
                var desktop = VirtualDesktop.DesktopOf(w.Handle, _options.Log)?.ToString("D") ?? "unknown";
                lines.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"{w.Id} task={w.ShownTask ?? "-"} desktop={desktop} here={IsHere(w)} state={w.WindowState} bounds={w.Bounds.X},{w.Bounds.Y},{w.Bounds.Width}x{w.Bounds.Height}");
                lines.AppendLine();
            }
            return lines.Length == 0 ? "no windows" : lines.ToString().TrimEnd();
        }

        public string MoveWindow(string windowId, Guid desktop)
        {
            var window = Open.FirstOrDefault(w => w.Id == windowId) ?? throw new InvalidOperationException($"no window {windowId}");
            if (!VirtualDesktop.TryMove(window.Handle, desktop, _options.Log)) throw new InvalidOperationException("Windows refused the move; see the log");
            ScheduleSave();
            return "ok";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hotkey?.Dispose();
                _saveTimer.Dispose();
                _tray.Dispose();
                _invoker.Dispose();
                _icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
