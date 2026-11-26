using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Ochre.Platform.Windows.Shell;

/// <summary>
/// Virtual desktops through the documented IVirtualDesktopManager: whether one of our windows is on the desktop the
/// user is looking at, and (a dev aid) moving one to another desktop so test runs don't land on the user's screen.
/// Windows only lets a process move its own windows.
/// </summary>
internal static partial class VirtualDesktop
{
    private static readonly Guid ManagerClsid = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");
    private static readonly Guid ManagerIid = new("a5cd92ff-29be-454c-8d04-d82879fb3f1b");

    /// <summary>
    /// Chromium throttles windows it thinks are hidden, which would make an off-screen window behave unlike a real
    /// one. These keep it rendering normally.
    /// </summary>
    public const string UnthrottledBrowserArguments =
        "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-renderer-backgrounding";

    public static bool TryMove(nint hwnd, Guid desktop, Action<string> log)
    {
        if (hwnd == 0) return false;
        var result = WithManager(m => m.MoveWindowToDesktop(hwnd, desktop), log);
        if (result is { } hr and not 0) log($"move to virtual desktop failed: 0x{hr:X8}");
        return result == 0;
    }

    /// <summary>
    /// Whether the window is on the desktop the user is looking at. When Windows can't say (no desktops, the
    /// service unavailable), it answers yes, so the caller falls back to reusing the window.
    /// </summary>
    public static bool IsOnCurrentDesktop(nint hwnd, Action<string> log)
    {
        if (hwnd == 0) return true;
        var onCurrent = 1;
        var result = WithManager(m => m.IsWindowOnCurrentVirtualDesktop(hwnd, out onCurrent), log);
        return result != 0 || onCurrent != 0;
    }

    private static int? WithManager(Func<IVirtualDesktopManager, int> call, Action<string> log)
    {
        var hr = CoCreateInstance(ManagerClsid, 0, 1 | 4 /* CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER */, ManagerIid, out var ptr);
        if (hr != 0)
        {
            log($"virtual desktop manager unavailable: 0x{hr:X8}");
            return null;
        }
        try
        {
            var manager = (IVirtualDesktopManager)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
            return call(manager);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [GeneratedComInterface]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    internal partial interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint hwnd, out int onCurrent);

        [PreserveSig]
        int GetWindowDesktopId(nint hwnd, out Guid desktop);

        [PreserveSig]
        int MoveWindowToDesktop(nint hwnd, in Guid desktop);
    }
}
