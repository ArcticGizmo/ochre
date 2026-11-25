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

    public IWindowSession Connect(Action<string> post)
    {
        var dispatcher = new BridgeDispatcher(post);
        dispatcher.UnexpectedError += (type, ex) => _host.Log.Error($"bridge {type} failed", ex);
        AppApi.Register(dispatcher, _host);
        var session = new Session(this, dispatcher, post);
        lock (_gate) _sessions.Add(session);
        return session;
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
