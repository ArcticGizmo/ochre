using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spike.Core;

/// <summary>
/// Lets unattended bench runs open their window on another virtual desktop. Windows only allows a process to
/// move its own windows, which is all the spike needs.
/// </summary>
public static partial class VirtualDesktop
{
    private static readonly Guid ManagerClsid = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");
    private static readonly Guid ManagerIid = new("a5cd92ff-29be-454c-8d04-d82879fb3f1b");

    /// <summary>The desktop from OCHRE_SPIKE_DESKTOP, if set.</summary>
    public static Guid? Configured { get; } =
        Guid.TryParse(Environment.GetEnvironmentVariable("OCHRE_SPIKE_DESKTOP"), out var g) ? g : null;

    /// <summary>
    /// Chromium throttles windows it thinks are hidden, which would flatter the off-screen numbers. These switch
    /// that off so a window on another desktop renders as if it were visible.
    /// </summary>
    public static string? BrowserArguments => Configured is null
        ? null
        : "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-renderer-backgrounding";

    public static bool TryMoveToConfigured(nint hwnd, Action<string> log)
    {
        if (Configured is not { } desktop || hwnd == 0) return false;
        var hr = CoCreateInstance(ManagerClsid, 0, 1 /* CLSCTX_INPROC_SERVER */, ManagerIid, out var ptr);
        if (hr != 0)
        {
            hr = CoCreateInstance(ManagerClsid, 0, 4 /* CLSCTX_LOCAL_SERVER */, ManagerIid, out ptr);
            if (hr != 0)
            {
                log($"virtual desktop manager unavailable: 0x{hr:X8}");
                return false;
            }
        }
        try
        {
            var manager = (IVirtualDesktopManager)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
            hr = manager.MoveWindowToDesktop(hwnd, desktop);
            log(hr == 0 ? $"window moved to desktop {desktop}" : $"move to desktop {desktop} failed: 0x{hr:X8}");
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
