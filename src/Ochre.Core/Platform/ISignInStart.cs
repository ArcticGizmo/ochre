namespace Ochre.Core.Platform;

/// <summary>
/// Starting Ochre in the tray when the user signs in. Only an installed copy offers it: a dev build's path changes
/// with every checkout. Best-effort; never throws.
/// </summary>
public interface ISignInStart
{
    bool IsOn { get; }

    void Set(bool on);
}
