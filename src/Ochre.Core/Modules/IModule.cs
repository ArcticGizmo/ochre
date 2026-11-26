using System.Text.Json.Serialization.Metadata;
using Ochre.Core.Attention;
using Ochre.Core.Bridge;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;

namespace Ochre.Core.Modules;

/// <summary>
/// A feature the kernel works without (ADR 0007, 0012). Constructing one does nothing; tools can list modules and
/// their contracts without starting them.
/// </summary>
internal interface IModule
{
    /// <summary>Stable, lowercase; also the prefix of its bridge messages and events (<c>terminals.open</c>).</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>One or two sentences for Settings: what turning it on gives you.</summary>
    string Description { get; }

    bool DefaultEnabled { get; }

    IReadOnlyList<RequestContract> Requests { get; }

    IReadOnlyList<EventContract> Events { get; }

    /// <summary>Starts the module; disposing the result stops it. Throwing leaves the module off for this run.</summary>
    IDisposable Start(IModuleContext context);
}

/// <summary>Everything the kernel offers a module. A module reaches nothing else of the host.</summary>
internal interface IModuleContext
{
    AppLog Log { get; }

    /// <summary>A local (not roaming) folder of the module's own; created on first use by the module.</summary>
    string DataFolder { get; }

    TaskStore Tasks { get; }

    SessionManager Sessions { get; }

    SessionLedger Ledger { get; }

    /// <summary>The attention inbox (ADR 0013). Throws on a host without one.</summary>
    AttentionKernel Attention { get; }

    /// <summary>Brings up a window on the current desktop showing <paramref name="taskId"/>, or any window for null.</summary>
    void ShowTask(string? taskId);

    /// <summary>A platform service by its Core interface (<c>ITerminalPlatform</c>, <c>IDataProtector</c>, ...).</summary>
    T Service<T>()
        where T : class;

    /// <summary>Registers the module's bridge handlers on every window's dispatcher, now and for windows opened later.</summary>
    void AddBridge(Action<BridgeDispatcher> register);

    /// <summary>A window closed; its dispatcher (the one <see cref="AddBridge"/> was given) is a key for per-window state.</summary>
    void OnWindowClosed(Action<BridgeDispatcher> handler);

    /// <summary>Sends an event to every open window. Safe from any thread.</summary>
    void Broadcast<T>(string name, T payload, JsonTypeInfo<T> info);

    /// <summary>Windows is ending the session; there are a few seconds at most, and the shutdown may yet be cancelled.</summary>
    void OnSessionEnding(Action handler);

    /// <summary>Offers a way to make a checkout for a task ("Start agent in a new worktree").</summary>
    void AddWorktreeProvider(IWorktreeProvider provider);
}
