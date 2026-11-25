namespace Ochre.Platform.Windows.Shell;

public sealed record ShellOptions(
    string Title,
    string UiFolder,
    string WebViewDataFolder,
    Action<string> Log)
{
    /// <summary>Load the UI from a Vite dev server instead of disk (dev builds only).</summary>
    public Uri? DevServer { get; init; }

    /// <summary>Hide the window on close so it reopens instantly, at the cost of keeping the WebView's memory.</summary>
    public bool HideOnClose { get; init; }

    /// <summary>Dev aid: open windows unfocused on this virtual desktop so test runs don't land on the user's screen.</summary>
    public Guid? VirtualDesktop { get; init; }

    public bool EnableDevTools { get; init; }
}
