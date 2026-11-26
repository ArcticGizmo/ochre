using System.Diagnostics;
using Ochre.Core.Bridge;
using Ochre.Core.Hosting;
using Ochre.Host.Bridge;

namespace Ochre.Host;

/// <summary>Gives each loaded page its own bridge, and broadcasts host events to every open page.</summary>
internal sealed class WindowHost : IWindowHost
{
    private readonly OchreHost _host;
    private readonly Lock _gate = new();
    private readonly List<Session> _sessions = [];
    private long _openRequestedAt;

    public WindowHost(OchreHost host) => _host = host;

    public void TrayReady()
    {
        using var self = Process.GetCurrentProcess();
        _host.Log.Info($"tray ready {(DateTime.Now - self.StartTime).TotalMilliseconds:F0} ms after process start");
    }

    public void WindowOpenRequested() => Interlocked.Exchange(ref _openRequestedAt, Stopwatch.GetTimestamp());

    /// <summary>Called by the page once it has rendered, to log how long the open took.</summary>
    public void PageReady()
    {
        var requested = Interlocked.Exchange(ref _openRequestedAt, 0);
        if (requested != 0) _host.Log.Info($"window ready {Stopwatch.GetElapsedTime(requested).TotalMilliseconds:F0} ms after open request");
    }

    public bool TaskExists(string taskId) => _host.Tasks.Get(taskId) is not null;

    public IWindowSession Connect(IShellWindow window, Action<string> post)
    {
        var dispatcher = new BridgeDispatcher(post);
        dispatcher.UnexpectedError += (type, ex) => _host.Log.Error($"bridge {type} failed", ex);
        AppApi.Register(dispatcher, _host);
        WindowApi.Register(dispatcher, _host, window);
        TaskApi.Register(dispatcher, _host.Tasks, _host.Parking);
        SessionApi.Register(dispatcher, _host.Sessions);
        WorktreeApi.Register(dispatcher, _host.Tasks, _host.Modules, _host.Sessions);
        _host.Modules.RegisterBridge(dispatcher);
        CheckAgainstContract(dispatcher);
        var session = new Session(this, dispatcher, post);
        lock (_gate) _sessions.Add(session);
        return session;
    }

    private bool _contractChecked;

    /// <summary>
    /// A handler missing from the kernel's or a running module's contract (or the reverse) means the generated UI
    /// types are stale (ADR 0009, 0012).
    /// </summary>
    private void CheckAgainstContract(BridgeDispatcher dispatcher)
    {
        if (_contractChecked) return;
        _contractChecked = true;
        var declared = BridgeContract.Requests.Concat(_host.Modules.RunningRequests).Select(r => r.Type).ToHashSet(StringComparer.Ordinal);
        var registered = dispatcher.RegisteredTypes.ToHashSet(StringComparer.Ordinal);
        foreach (var missing in declared.Except(registered)) _host.Log.Error($"bridge contract declares '{missing}' but no handler is registered");
        foreach (var extra in registered.Except(declared)) _host.Log.Error($"bridge handler '{extra}' is not in BridgeContract; regenerate the UI types");
    }

    /// <summary>Sends an event to every open page. Safe from any thread.</summary>
    public void Broadcast(string serializedEvent)
    {
        Session[] targets;
        lock (_gate) targets = [.. _sessions];
        foreach (var s in targets) s.Post(serializedEvent);
    }

    private void Remove(Session session)
    {
        lock (_gate) _sessions.Remove(session);
    }

    private sealed class Session(WindowHost owner, BridgeDispatcher dispatcher, Action<string> post) : IWindowSession
    {
        public void Post(string message) => post(message);

        public Task ReceiveAsync(string? message) => dispatcher.DispatchAsync(message);

        public void Dispose() => owner.Remove(this);
    }
}
