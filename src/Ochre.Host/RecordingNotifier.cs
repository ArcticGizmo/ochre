using System.Text;
using Ochre.Core.Platform;

namespace Ochre.Host;

/// <summary>
/// Stands in for desktop notifications in a headless dev run, so a test never puts one on the user's screen: it
/// keeps what would be showing, and <c>ochre-cli dev notifications</c> / <c>dev notify</c> read it and press its
/// buttons as a click would.
/// </summary>
internal sealed class RecordingNotifier(Action<string> log) : IDesktopNotifier
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, DesktopNotification> _showing = new(StringComparer.Ordinal);

    public event Action<NotificationAction>? Activated;

    public void Show(DesktopNotification notification)
    {
        lock (_gate) _showing[notification.Tag] = notification;
        log($"notification (recorded, not shown) {notification.Tag}: {notification.Title}");
    }

    public void Remove(string tag, string group)
    {
        lock (_gate) _showing.Remove(tag);
    }

    public void Clear()
    {
        lock (_gate) _showing.Clear();
    }

    public void Dispose() => Clear();

    /// <summary>One block per notification showing: its tag, header, title, body, inputs and buttons.</summary>
    public string Describe()
    {
        DesktopNotification[] showing;
        lock (_gate) showing = [.. _showing.Values];
        if (showing.Length == 0) return "no notifications";
        var text = new StringBuilder();
        foreach (var n in showing)
        {
            text.AppendLine($"{n.Tag} [{n.Look}] {n.GroupTitle}");
            text.AppendLine($"  {n.Title}");
            if (n.Body.Length > 0) text.AppendLine($"  {n.Body}");
            foreach (var i in n.Inputs)
                text.AppendLine(i.Choices is { } choices ? $"  input {i.Id}: {string.Join(", ", choices.Select(c => $"{c.Key}={c.Value}"))}" : $"  input {i.Id}: \"{i.Placeholder}\"");
            text.AppendLine($"  buttons: {string.Join(", ", n.Buttons.Select(b => $"{b.Label} ({b.Action})"))}");
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>Clicks a button (or <c>open</c>) on the notification with this tag, with input values.</summary>
    public string Press(string tag, string action, IReadOnlyDictionary<string, string> inputs)
    {
        lock (_gate)
        {
            if (!_showing.TryGetValue(tag, out var n)) return $"error: no notification {tag}";
            if (action != "open" && n.Buttons.All(b => b.Action != action)) return $"error: {tag} has no '{action}' button";
            _showing.Remove(tag);   // Windows takes a notification down when a button is pressed
        }
        Activated?.Invoke(new NotificationAction(tag, action, inputs));
        return "ok";
    }
}
