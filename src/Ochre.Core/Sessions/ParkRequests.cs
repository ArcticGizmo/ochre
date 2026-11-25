using System.Collections.Concurrent;

namespace Ochre.Core.Sessions;

/// <summary>
/// Which agents have been asked for a park note and haven't given one yet. <c>task park-note</c> runs without a
/// permission prompt (ADR 0010), so an agent may write one only while its task is being parked, and only once.
/// </summary>
public sealed class ParkRequests
{
    private readonly ConcurrentDictionary<(string Task, string Session), bool> _open = new();

    public void Open(string taskId, string sessionId) => _open[(taskId, sessionId)] = true;

    public void Close(string taskId, string sessionId) => _open.TryRemove((taskId, sessionId), out _);

    public bool IsOpen(string taskId, string sessionId) => _open.ContainsKey((taskId, sessionId));

    /// <summary>Closes the request and says whether it was open: the one note an agent may write.</summary>
    public bool TryTake(string taskId, string sessionId) => _open.TryRemove((taskId, sessionId), out _);

    public IReadOnlyList<string> OpenFor(string taskId) => _open.Keys.Where(k => k.Task == taskId).Select(k => k.Session).ToList();
}
