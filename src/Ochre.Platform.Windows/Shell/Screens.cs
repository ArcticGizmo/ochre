using System.Runtime.InteropServices;
using Ochre.Core.Layout;

namespace Ochre.Platform.Windows.Shell;

/// <summary>The monitors as <see cref="WindowRestore"/> sees them, with each one's own scale.</summary>
internal static partial class Screens
{
    public static IReadOnlyList<ScreenArea> All() =>
        Screen.AllScreens.Select(s => new ScreenArea(Rect(s.Bounds), Rect(s.WorkingArea), ScaleOf(s.Bounds), s.Primary)).ToList();

    public static PixelRect Rect(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    public static Rectangle Rect(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>The scale of the monitor nearest the middle of <paramref name="bounds"/>; 1 when Windows can't say.</summary>
    public static double ScaleOf(Rectangle bounds)
    {
        var monitor = MonitorFromPoint(new PointStruct(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), 2 /* MONITOR_DEFAULTTONEAREST */);
        return monitor != 0 && GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PointStruct(int X, int Y);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(PointStruct pt, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
