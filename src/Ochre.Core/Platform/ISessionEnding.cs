namespace Ochre.Core.Platform;

/// <summary>The OS telling Ochre the user's session is about to end (shutdown, restart or sign-out).</summary>
public interface ISessionEnding : IDisposable
{
    /// <summary>
    /// Raised when the OS asks to end the session, before it does. Handlers have a few seconds at most and must not
    /// block it; the session may still be cancelled afterwards (another app refuses, or the user changes their mind).
    /// </summary>
    event Action? Ending;
}
