namespace Ochre.Core.Hosting;

/// <summary>
/// The OS-specific part of the app: tray icon, windows and the WebView inside them. Everything stateful lives in
/// the host; a shell only shows it. Implemented per platform (WinForms + WebView2 on Windows).
/// </summary>
public interface IShell
{
    /// <summary>Runs the tray and message loop on the calling thread until <see cref="Quit"/>.</summary>
    void Run(bool openWindow);

    /// <summary>Opens or activates the window. Safe from any thread.</summary>
    void OpenWindow();

    /// <summary>Closes the window; the app keeps running in the tray. Safe from any thread.</summary>
    void CloseWindow();

    /// <summary>Closes every window and ends <see cref="Run"/>. Safe from any thread.</summary>
    void Quit();
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
    IWindowSession Connect(Action<string> post);

    void TrayReady();
}

public interface IWindowSession : IDisposable
{
    /// <summary>A message from the page. Untrusted.</summary>
    Task ReceiveAsync(string? message);
}
