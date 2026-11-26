using Ochre.Core;
using Ochre.Core.Bridge;
using Ochre.Core.Modules;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// The module host's promises (ADR 0007, 0012): only modules switched on start, a module that fails to start is left
/// off without taking anything else down, and modules stop in reverse order.
/// </summary>
public sealed class ModuleHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ochre-tests", Guid.NewGuid().ToString("N"));
    private readonly AppLog _log;
    private readonly TaskStore _store;
    private readonly SessionManager _sessions;
    private readonly List<string> _events = [];
    private readonly List<string> _broadcasts = [];

    public ModuleHostTests()
    {
        _log = new AppLog(Path.Combine(_root, "logs"));
        _store = new TaskStore(Path.Combine(_root, "tasks"), _log);
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _sessions = new SessionManager(_store, new SessionLedger(Path.Combine(_root, "sessions.json")), _log, OchreProfile.Current, new SessionSystem
        {
            Launcher = new FakeClaudeLauncher(),
            LocateCli = () => @"C:\fake\bin\ochre.com",
            IsTrusted = _ => true,
            GrantTrust = trusted.Add,
            FindLive = _ => null,
            ReadLive = () => [],
            ResolveTranscript = (_, _) => null,
            FolderExists = Directory.Exists,
        });
    }

    public void Dispose()
    {
        _sessions.Dispose();
        _store.Dispose();
        _log.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ModuleHost Host(IReadOnlyList<IModule> modules, Func<string, bool?> enabled) =>
        new(modules, enabled, new ModuleKernel(_log, _root, _store, _sessions, new SessionLedger(Path.Combine(_root, "sessions.json")), new Dictionary<Type, object>(), _broadcasts.Add));

    [Fact]
    public void Only_modules_switched_on_start_and_a_choice_overrides_the_default()
    {
        var a = new FakeModule("a", _events, defaultOn: true);
        var b = new FakeModule("b", _events, defaultOn: false);
        var c = new FakeModule("c", _events, defaultOn: true);
        using var host = Host([a, b, c], id => id == "c" ? false : null);
        host.StartAll();

        Assert.Equal(["a"], host.RunningIds);
        Assert.Equal(["start a"], _events);
        var states = host.States.ToDictionary(s => s.Id);
        Assert.True(states["a"].Running);
        Assert.False(states["b"].Enabled);
        Assert.False(states["c"].Enabled);
    }

    [Fact]
    public void A_module_that_fails_to_start_is_left_off_with_its_contributions_dropped()
    {
        var broken = new FakeModule("broken", _events, defaultOn: true) { ThrowOnStart = true };
        var fine = new FakeModule("fine", _events, defaultOn: true);
        using var host = Host([broken, fine], _ => null);
        host.StartAll();

        Assert.Equal(["fine"], host.RunningIds);
        var state = host.States.Single(s => s.Id == "broken");
        Assert.False(state.Running);
        Assert.Equal("boom", state.Error);

        var dispatcher = new BridgeDispatcher(_ => { });
        host.RegisterBridge(dispatcher);
        Assert.Equal(["fine.ping"], dispatcher.RegisteredTypes);
        host.SessionEnding();
        Assert.Contains("ending fine", _events);
        Assert.DoesNotContain("ending broken", _events);
        Assert.Equal(["fine-tree"], host.WorktreeProviders.Select(p => p.Id));
        Assert.Null(host.WorktreeProvider("broken-tree"));
    }

    [Fact]
    public void Modules_stop_in_reverse_order()
    {
        using (var host = Host([new FakeModule("a", _events, true), new FakeModule("b", _events, true)], _ => null))
        {
            host.StartAll();
        }
        Assert.Equal(["start a", "start b", "stop b", "stop a"], _events);
    }

    [Fact]
    public void A_module_may_only_send_events_under_its_own_name()
    {
        var module = new FakeModule("a", _events, true);
        using var host = Host([module], _ => null);
        host.StartAll();
        module.Context!.Broadcast("a.changed", new Empty(), BridgeTestJson.Default.Empty);
        Assert.Single(_broadcasts);
        Assert.Throws<ArgumentException>(() => module.Context.Broadcast("tasks.changed", new Empty(), BridgeTestJson.Default.Empty));
    }

    private sealed class FakeModule(string id, List<string> events, bool defaultOn) : IModule
    {
        public string Id => id;
        public string Name => id;
        public string Description => "";
        public bool DefaultEnabled => defaultOn;
        public IReadOnlyList<RequestContract> Requests { get; } = [new($"{id}.ping", typeof(Empty), typeof(Ack))];
        public IReadOnlyList<EventContract> Events { get; } = [];
        public bool ThrowOnStart { get; init; }
        public IModuleContext? Context { get; private set; }

        public IDisposable Start(IModuleContext context)
        {
            Context = context;
            context.AddBridge(b => b.Handle($"{id}.ping", BridgeTestJson.Default.Empty, BridgeTestJson.Default.Ack, _ => new Ack()));
            context.OnSessionEnding(() => events.Add($"ending {id}"));
            context.AddWorktreeProvider(new FakeProvider($"{id}-tree"));
            if (ThrowOnStart) throw new InvalidOperationException("boom");
            events.Add($"start {id}");
            return new Stop(() => events.Add($"stop {id}"));
        }
    }

    private sealed class FakeProvider(string id) : IWorktreeProvider
    {
        public string Id => id;
        public string Name => id;
        public string RemoveLabel => "Remove";
        public string RemoveDescription => "";
        public Task<WorktreePlan> PlanAsync(string repo, string branch, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorktreePlan> CreateAsync(string repo, string branch, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorktreeCheck> CheckAsync(TaskWorktree worktree, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAsync(TaskWorktree worktree, bool discardChanges, bool deleteBranch, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Stop(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Empty))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Ack))]
internal sealed partial class BridgeTestJson : System.Text.Json.Serialization.JsonSerializerContext;
