namespace Ochre.Core.Platform;

public enum NotificationLook
{
    /// <summary>Something is blocked on the user; stays on screen until acted on.</summary>
    NeedsYou,
    Finished,
    Failed,
    Waiting,
}

public enum NotificationButtonStyle { Plain, Positive, Negative }

/// <param name="Action">What pressing it does, as the notifier hands back in <see cref="NotificationAction"/>.</param>
/// <param name="InputId">Places the button beside that input (a reply box's Send).</param>
public sealed record NotificationButton(string Label, string Action, NotificationButtonStyle Style = NotificationButtonStyle.Plain, string? InputId = null);

/// <param name="Choices">For a choice box: (id, label) pairs; null for a text box.</param>
public sealed record NotificationInput(string Id, string Placeholder, IReadOnlyList<KeyValuePair<string, string>>? Choices = null);

/// <summary>
/// One desktop notification. <paramref name="Tag"/> names it: showing another with the same tag replaces it.
/// <paramref name="Group"/> and <paramref name="GroupTitle"/> gather notifications under a header (a task).
/// </summary>
/// <param name="Attribution">A quiet last line, e.g. the folder it runs in.</param>
public sealed record DesktopNotification(
    string Tag,
    string Group,
    string GroupTitle,
    string Title,
    string Body,
    string? Attribution,
    NotificationLook Look,
    IReadOnlyList<NotificationButton> Buttons,
    IReadOnlyList<NotificationInput> Inputs);

/// <param name="Action">The button's action, or <c>open</c> for a click on the notification itself.</param>
/// <param name="Inputs">What was typed or chosen, by input id.</param>
public sealed record NotificationAction(string Tag, string Action, IReadOnlyDictionary<string, string> Inputs);

/// <summary>
/// Shows desktop notifications and reports clicks on them. The OS and whoever clicks are outside Ochre's control, so
/// a <see cref="NotificationAction"/> is only a request: the caller checks it against what is current. Never throws
/// from <see cref="Show"/>, <see cref="Remove"/> or <see cref="Clear"/>; a notifier that can't show says so in the log.
/// </summary>
public interface IDesktopNotifier : IDisposable
{
    /// <summary>A button or the notification was clicked. Raised from any thread.</summary>
    event Action<NotificationAction>? Activated;

    /// <summary>Shows it, replacing one with the same tag.</summary>
    void Show(DesktopNotification notification);

    void Remove(string tag, string group);

    /// <summary>Removes every notification this app has shown, including ones left from an earlier run.</summary>
    void Clear();
}
