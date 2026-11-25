using Microsoft.Win32;
using Ochre.Core.Platform;

namespace Ochre.Platform.Windows;

/// <summary>WM_QUERYENDSESSION, through <see cref="SystemEvents.SessionEnding"/>. Never cancels the end itself.</summary>
public sealed class SessionEnding : ISessionEnding
{
    public SessionEnding() => SystemEvents.SessionEnding += OnSessionEnding;

    public event Action? Ending;

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Ending?.Invoke();

    public void Dispose() => SystemEvents.SessionEnding -= OnSessionEnding;
}
