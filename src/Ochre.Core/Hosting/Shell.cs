namespace Ochre.Core.Hosting;

/// <summary>
/// The OS-specific part of the app: tray icon, windows and the WebView inside them. Everything stateful lives in
/// the host; a shell only shows it. Implemented per platform (WinForms + WebView2 on Windows).
/// </summary>
public interface IShell
{
    /// <summary>Runs the tray and message loop on the calling thread until <see cref="Quit"/>.</summary>
    void Run(bool openWindow);

    /// <summary>
    /// Activates a window on the user's current virtual desktop, or opens a new one there; never switches desktops.
    /// The first call also reopens the windows saved when Ochre last quit, each on its own desktop. Safe from any
    /// thread.
    /// </summary>
    void OpenWindow();

    /// <summary>
    /// Shows a task in a window on the current desktop: brings forward one already showing it here, or opens a new
    /// one. When the only windows showing it are on other desktops, <paramref name="choice"/> decides; the shell never
    /// switches desktops. <paramref name="from"/> (the window that asked) doesn't count as showing it. Safe from any
    /// thread.
    /// </summary>
    Task<OpenTaskOutcome> OpenTaskAsync(string taskId, OpenTaskChoice choice, IShellWindow? from);

    /// <summary>Closes every window; the app keeps running in the tray. Safe from any thread.</summary>
    void CloseWindow();

    /// <summary>
    /// Registers <paramref name="hotkey"/> system-wide (replacing any earlier one; null for none). Pressing it brings
    /// forward a window on the current desktop, or opens one, and shows the "jump to task" picker there. Returns why it
    /// couldn't be registered (another app has it), or null. Safe from any thread.
    /// </summary>
    Task<string?> SetJumpHotkeyAsync(Hotkey? hotkey);

    /// <summary>Saves the window layout, closes every window and ends <see cref="Run"/>. Safe from any thread.</summary>
    void Quit();

    /// <summary>A short line added to the tray icon's tooltip ("2 need you"), or null for none. Safe from any thread.</summary>
    void SetTrayStatus(string? status);
}

public enum OpenTaskChoice
{
    /// <summary>Answer <see cref="OpenTaskOutcome.Elsewhere"/> rather than pick for the user.</summary>
    Ask,

    /// <summary>Move the window showing it on another desktop to this one.</summary>
    Bring,

    /// <summary>Open another window for it here.</summary>
    New,
}

public enum OpenTaskOutcome
{
    Opened,
    Focused,
    Moved,

    /// <summary>Only windows on other desktops show it; ask the user.</summary>
    Elsewhere,
}

/// <summary>One window, as the host's bridge sees it.</summary>
public interface IShellWindow
{
    /// <summary>Short and unique while the host runs, e.g. <c>w2</c>.</summary>
    string Id { get; }

    /// <summary>The task the window opened on (restored, or opened for a task), for the page to show first.</summary>
    string? InitialTask { get; }

    /// <summary>The task the page shows now, as it last reported. Safe from any thread.</summary>
    void ReportTask(string? taskId);

    /// <summary>True once if the window was opened by the jump hotkey, so its page shows the picker when it loads.</summary>
    bool TakeJumpRequest();
}

/// <summary>
/// Dev-build hooks so a change can be checked in the real window without a person at the screen: run a script in
/// the page and capture what it shows. Never wired up in release builds.
/// </summary>
public interface IShellDevTools
{
    /// <summary>
    /// Runs <paramref name="script"/> in a window's page (<paramref name="windowId"/>, or the most recently used) and
    /// returns its result as JSON.
    /// </summary>
    Task<string> EvalAsync(string script, string? windowId = null);

    /// <summary>Writes a PNG of a window's page (<paramref name="windowId"/>, or the most recently used) to <paramref name="path"/>.</summary>
    Task CaptureAsync(string path, string? windowId = null);

    /// <summary>One line per open window: id, task, desktop, whether it's on the current desktop, and bounds.</summary>
    Task<string> ListWindowsAsync();

    /// <summary>Moves a window to a virtual desktop, as the user would from Task View.</summary>
    Task<string> MoveWindowAsync(string windowId, Guid desktop);

    /// <summary>Does what pressing the jump hotkey does, for a dev run that doesn't register it.</summary>
    void PressJumpHotkey();
}

/// <summary>What a shell calls back into: one session per loaded page.</summary>
public interface IWindowHost
{
    /// <summary>Called when the user asked for a window, so the host can time open-to-ready.</summary>
    void WindowOpenRequested();

    /// <summary>
    /// Called once a window's page is wired up. <paramref name="post"/> sends a message to that page and is safe
    /// from any thread.
    /// </summary>
    IWindowSession Connect(IShellWindow window, Action<string> post);

    /// <summary>Whether a task id names a task that exists, so a saved window's task can be dropped when it's gone.</summary>
    bool TaskExists(string taskId);

    void TrayReady();
}

public interface IWindowSession : IDisposable
{
    /// <summary>A message from the page. Untrusted.</summary>
    Task ReceiveAsync(string? message);
}
