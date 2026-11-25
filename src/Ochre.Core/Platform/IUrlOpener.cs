namespace Ochre.Core.Platform;

/// <summary>
/// Opens a web URL in the user's browser. Only what <see cref="OpenTargets.WebUrl"/> accepts is opened; anything
/// else silently no-ops. Best-effort; never throws.
/// </summary>
public interface IUrlOpener
{
    /// <summary>The plain shell open, which reuses the browser's last-focused window.</summary>
    void Open(string url);

    /// <summary>
    /// A fresh browser window (the middle-click behaviour), created on the current virtual desktop rather than
    /// activating an existing window elsewhere. Falls back to <see cref="Open"/>.
    /// </summary>
    void OpenInNewWindow(string url);
}
