using System.Text.Json.Serialization.Metadata;
using Ochre.Core.Bridge;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;

namespace Ochre.Core.Modules;

/// <param name="Running">Started this run. A module turned on in Settings stays off until the next start.</param>
/// <param name="Error">Why it failed to start, if it did.</param>
internal sealed record ModuleState(string Id, string Name, string Description, bool Enabled, bool Running, string? Error);

/// <summary>What the kernel hands every module (ADR 0012).</summary>
internal sealed record ModuleKernel(
    AppLog Log,
    string DataRoot,
    TaskStore Tasks,
    SessionManager Sessions,
    SessionLedger Ledger,
    IReadOnlyDictionary<Type, object> Services,
    Action<string> Broadcast);

/// <summary>
/// Starts the modules that are switched on, after the kernel, and stops them in reverse on quit. A module that
/// throws while starting is logged and left off with its contributions dropped; the host carries on.
/// </summary>
internal sealed class ModuleHost : IDisposable
{
    private readonly IReadOnlyList<IModule> _modules;
    private readonly Func<string, bool?> _enabled;
    private readonly ModuleKernel _kernel;
    private readonly List<Running> _running = [];
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private bool _started;

    public ModuleHost(IReadOnlyList<IModule> modules, Func<string, bool?> enabled, ModuleKernel kernel)
    {
        _modules = modules;
        _enabled = enabled;
        _kernel = kernel;
    }

    public bool IsEnabled(IModule module) => _enabled(module.Id) ?? module.DefaultEnabled;

    public IReadOnlyList<ModuleState> States => _modules
        .Select(m => new ModuleState(m.Id, m.Name, m.Description, IsEnabled(m), _running.Any(r => r.Module == m), _errors.GetValueOrDefault(m.Id)))
        .ToList();

    /// <summary>The ids of the modules running this session.</summary>
    public IReadOnlyList<string> RunningIds => _running.Select(r => r.Module.Id).ToList();

    /// <summary>The bridge requests of the running modules, for checking registrations against.</summary>
    public IEnumerable<RequestContract> RunningRequests => _running.SelectMany(r => r.Module.Requests);

    public void StartAll()
    {
        if (_started) throw new InvalidOperationException("modules already started");
        _started = true;
        foreach (var module in _modules)
        {
            if (!IsEnabled(module)) continue;
            var context = new Context(_kernel, module);
            try
            {
                var stop = module.Start(context);
                _running.Add(new Running(module, context, stop));
                _kernel.Log.Info($"module {module.Id} started");
            }
            catch (Exception ex)
            {
                // Any failure is the module's; the kernel must keep working without it (ADR 0007).
                _errors[module.Id] = ex.Message;
                _kernel.Log.Error($"module {module.Id} failed to start", ex);
            }
        }
    }

    /// <summary>Registers every running module's handlers on a window's dispatcher.</summary>
    public void RegisterBridge(BridgeDispatcher dispatcher)
    {
        foreach (var r in _running)
        {
            foreach (var register in r.Context.Bridges) register(dispatcher);
        }
    }

    /// <summary>Windows is ending the session. Each handler runs in turn; one failing doesn't stop the rest.</summary>
    public void SessionEnding()
    {
        foreach (var r in _running)
        {
            foreach (var handler in r.Context.SessionEndingHandlers)
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    _kernel.Log.Error($"module {r.Module.Id} failed handling the session ending", ex);
                }
            }
        }
    }

    public void Dispose()
    {
        for (var i = _running.Count - 1; i >= 0; i--)
        {
            try
            {
                _running[i].Stop.Dispose();
            }
            catch (Exception ex)
            {
                _kernel.Log.Error($"module {_running[i].Module.Id} failed to stop", ex);
            }
        }
        _running.Clear();
    }

    private sealed record Running(IModule Module, Context Context, IDisposable Stop);

    private sealed class Context(ModuleKernel kernel, IModule module) : IModuleContext
    {
        public List<Action<BridgeDispatcher>> Bridges { get; } = [];
        public List<Action> SessionEndingHandlers { get; } = [];

        public AppLog Log => kernel.Log;
        public string DataFolder => Path.Combine(kernel.DataRoot, module.Id);
        public TaskStore Tasks => kernel.Tasks;
        public SessionManager Sessions => kernel.Sessions;
        public SessionLedger Ledger => kernel.Ledger;

        public T Service<T>()
            where T : class =>
            kernel.Services.TryGetValue(typeof(T), out var s) && s is T service
                ? service
                : throw new InvalidOperationException($"no {typeof(T).Name} on this platform");

        public void AddBridge(Action<BridgeDispatcher> register) => Bridges.Add(register);

        public void Broadcast<T>(string name, T payload, JsonTypeInfo<T> info)
        {
            if (!name.StartsWith(module.Id + ".", StringComparison.Ordinal))
                throw new ArgumentException($"module {module.Id} events start with '{module.Id}.'", nameof(name));
            kernel.Broadcast(BridgeDispatcher.SerializeEvent(name, payload, info));
        }

        public void OnSessionEnding(Action handler) => SessionEndingHandlers.Add(handler);
    }
}
