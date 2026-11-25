using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Ochre.Platform.Windows.Shell;

/// <summary>
/// Dev aid: moves one of our own windows to another virtual desktop so test runs don't land on the user's screen.
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
        var hr = CoCreateInstance(ManagerClsid, 0, 1 | 4 /* CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER */, ManagerIid, out var ptr);
        if (hr != 0)
        {
            log($"virtual desktop manager unavailable: 0x{hr:X8}");
            return false;
        }
        try
        {
            var manager = (IVirtualDesktopManager)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
            hr = manager.MoveWindowToDesktop(hwnd, desktop);
            if (hr != 0) log($"move to virtual desktop failed: 0x{hr:X8}");
            return hr == 0;
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
