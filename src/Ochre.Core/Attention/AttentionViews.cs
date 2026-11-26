namespace Ochre.Core.Attention;

/// <summary>
/// What each window has in front of the user, as its page reports it: whether the window has focus, whether it shows
/// the inbox, and which sessions' threads it shows. Lets a desktop notification stay quiet about something already
/// on screen (Perch's <c>AttentionSeen</c> rule). Windows are told apart by any key object their bridge holds.
/// </summary>
internal sealed class AttentionViews
{
    private readonly Lock _gate = new();
    private readonly Dictionary<object, View> _views = new(ReferenceEqualityComparer.Instance);

    /// <summary>A window's view changed. Raised outside the lock.</summary>
    public event Action? Changed;

    public void Set(object window, bool focused, bool inbox, IReadOnlyCollection<string> sessionIds)
    {
        var view = new View(focused, inbox, [.. sessionIds]);
        lock (_gate)
        {
            if (_views.TryGetValue(window, out var old) && old.Same(view)) return;
            _views[window] = view;
        }
        Changed?.Invoke();
    }

    public void Remove(object window)
    {
        bool removed;
        lock (_gate) removed = _views.Remove(window);
        if (removed) Changed?.Invoke();
    }

    /// <summary>The focused window shows the inbox or this session's thread.</summary>
    public bool InView(string sessionId)
    {
        lock (_gate) return _views.Values.Any(v => v.Focused && (v.Inbox || v.Sessions.Contains(sessionId)));
    }

    private sealed record View(bool Focused, bool Inbox, HashSet<string> Sessions)
    {
        public bool Same(View other) => Focused == other.Focused && Inbox == other.Inbox && Sessions.SetEquals(other.Sessions);
    }
}
