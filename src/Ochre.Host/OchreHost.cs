using Ochre.Core;
using Ochre.Core.Hosting;

namespace Ochre.Host;

/// <summary>
/// Owns everything stateful (ADR 0008). Windows are views: closing the last one leaves the host and everything
/// it owns running until the user quits from the tray.
/// </summary>
internal sealed class OchreHost : IDisposable
{
    public OchreHost(OchreProfile profile, OchrePaths paths, AppLog log)
    {
        Profile = profile;
        Paths = paths;
        Log = log;
        Windows = new WindowHost(this);
    }

    public OchreProfile Profile { get; }
    public OchrePaths Paths { get; }
    public AppLog Log { get; }
    public WindowHost Windows { get; }
    public IShell? Shell { get; private set; }

    public void Attach(IShell shell) => Shell = shell;

    public void Dispose()
    {
    }
}
