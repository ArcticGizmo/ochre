using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Spike.Core;

namespace Shell.Avalonia;

internal sealed class App(SpikeHost host, string pipeName, CancellationToken stopping) : Application
{
    private readonly bool _hideOnClose = Environment.GetEnvironmentVariable("OCHRE_SPIKE_HIDE_ON_CLOSE") == "1";
    private MainWindow? _window;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var open = new NativeMenuItem("Open");
        open.Click += (_, _) => OpenWindow();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();

        var tray = new TrayIcon
        {
            Icon = MakeIcon(),
            ToolTipText = "Ochre spike (Avalonia)",
            Menu = [open, quit],
            IsVisible = true,
        };
        tray.Clicked += (_, _) => OpenWindow();
        TrayIcon.SetIcons(this, [tray]);

        SingleInstance.Listen(pipeName, () => Dispatcher.UIThread.Post(OpenWindow), stopping);
        Dispatcher.UIThread.Post(host.MarkTrayReady, DispatcherPriority.Background);
        base.OnFrameworkInitializationCompleted();
    }

    private void OpenWindow()
    {
        host.MarkWindowOpenRequested();
        if (_window is not null)
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.ReportReadyIfLoaded();
            return;
        }
        _window = new MainWindow(host, _hideOnClose);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    private void Quit()
    {
        if (_window is not null) _window.AllowClose = true;
        _window?.Close();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private static WindowIcon MakeIcon()
    {
        var bitmap = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
        {
            var pixels = new int[16 * 16];
            Array.Fill(pixels, unchecked((int)0xFFCC7722));
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
        }
        return new WindowIcon(bitmap);
    }
}
