using System.Runtime.InteropServices;
using Ochre.Core.Hosting;

namespace Ochre.Platform.Windows.Shell;

/// <summary>
/// One system-wide hotkey through <c>RegisterHotKey</c>, owned by a message-only window on the UI thread. Windows
/// gives the process that receives a hotkey the right to bring its window to the front, which is what lets the jump
/// picker come up over whatever the user was doing.
/// </summary>
internal sealed partial class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int Id = 1;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly Action _pressed;
    private bool _registered;

    public GlobalHotkey(Action pressed)
    {
        _pressed = pressed;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) /* HWND_MESSAGE */ });
    }

    /// <summary>Replaces the registered hotkey; null for none. Returns why Windows refused it, or null.</summary>
    public string? Set(Hotkey? hotkey)
    {
        if (_registered)
        {
            UnregisterHotKey(Handle, Id);
            _registered = false;
        }
        if (hotkey is null) return null;
        var mods = ModNoRepeat | (hotkey.Ctrl ? ModControl : 0) | (hotkey.Alt ? ModAlt : 0) | (hotkey.Shift ? ModShift : 0) | (hotkey.Win ? ModWin : 0);
        if (RegisterHotKey(Handle, Id, mods, VirtualKey(hotkey.Key)))
        {
            _registered = true;
            return null;
        }
        var error = Marshal.GetLastPInvokeError();
        return error == ErrorHotkeyAlreadyRegistered
            ? $"Another app already uses {hotkey}."
            : $"Windows wouldn't register {hotkey} (error {error}).";
    }

    // Hotkey.Parse only allows letters, digits, F1 to F24 and Space.
    internal static uint VirtualKey(string key) => key switch
    {
        "Space" => 0x20,
        [var c] => c,
        _ => 0x70u + uint.Parse(key[1..], System.Globalization.CultureInfo.InvariantCulture) - 1,
    };

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && (int)m.WParam == Id) _pressed();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Set(null);
        DestroyHandle();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}
