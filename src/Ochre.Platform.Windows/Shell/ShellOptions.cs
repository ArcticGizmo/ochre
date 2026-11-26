namespace Ochre.Platform.Windows.Shell;

public sealed record ShellOptions(
    string Title,
    string UiFolder,
    string WebViewDataFolder,
    Action<string> Log)
{
    /// <summary>Load the UI from a Vite dev server instead of disk (dev builds only).</summary>
    public Uri? DevServer { get; init; }

    /// <summary>
    /// Dev aid: open windows unfocused on this virtual desktop so test runs don't land on the user's screen. It stands
    /// in for the user's current desktop: "here" is this desktop, and windows are brought to it rather than to the
    /// user's.
    /// </summary>
    public Guid? VirtualDesktop { get; init; }

    /// <summary>Dev aid: open windows off-screen, without a taskbar button or focus, so an agent can drive and capture
    /// them (<c>cli dev eval</c>, <c>cli dev capture</c>) without anything appearing on the user's screen.</summary>
    public bool Headless { get; init; }

    /// <summary>Windows are driven by a test run rather than the user: never take focus, never throttle the page.</summary>
    public bool Unattended => Headless || VirtualDesktop is not null;

    public bool EnableDevTools { get; init; }

    /// <summary>Where the open windows are saved for the next start (<c>windows.json</c>).</summary>
    public string? WindowLayoutFile { get; init; }
}
