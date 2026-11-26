using Ochre.Core.Modules;
using Ochre.Modules.Git;
using Ochre.Modules.Notifications;
using Ochre.Modules.Roost;
using Ochre.Modules.ShutdownPark;
using Ochre.Modules.Sprig;
using Ochre.Modules.Terminals;

namespace Ochre.Host;

/// <summary>
/// The first-party modules built into Ochre, in start order (ADR 0012). BridgeGen reads their contracts from here,
/// so the UI types cover every module whether it's on or off.
/// </summary>
internal static class ModuleCatalog
{
    public static IReadOnlyList<IModule> Create() =>
    [
        new GitModule(),
        new SprigModule(),
        new TerminalsModule(),
        new ShutdownParkModule(),
        new NotificationsModule(),
        new RoostModule(),
    ];
}
