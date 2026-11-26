using Ochre.Core;
using Ochre.Core.Attention;
using Ochre.Core.Modules;
using Ochre.Core.Platform;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;

namespace Ochre.Modules.Notifications;

/// <summary>
/// Keeps one notification per session in step with the attention board: shown when an item appears that isn't
/// already in front of the user, replaced as the session's item changes, removed when it clears or comes into view.
/// A click goes back through <see cref="AttentionActions"/>, which acts only on an item that is still current.
/// </summary>
internal sealed class NotificationSync(
    IDesktopNotifier notifier,
    AttentionKernel attention,
    SessionManager sessions,
    TaskStore tasks,
    Action<string?> showTask,
    AppLog log) : IDisposable
{
    private const string Outside = "outside";

    private readonly Lock _gate = new();
    // By session (the notification's tag): the item it shows and the group it was shown in.
    private readonly Dictionary<string, (string ItemId, string Group)> _shown = new(StringComparer.Ordinal);
    // Items that were in front of the user when they appeared; they never pop up, even after the user looks away.
    private readonly HashSet<string> _quiet = new(StringComparer.Ordinal);

    public void Start()
    {
        notifier.Clear();   // anything left by an earlier run names items that no longer exist
        notifier.Activated += OnActivated;
        attention.Board.Changed += Refresh;
        attention.Views.Changed += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        var show = new List<DesktopNotification>();
        var remove = new List<(string Tag, string Group)>();
        lock (_gate)
        {
            var items = attention.Board.Items;
            var current = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            _quiet.IntersectWith(current);

            foreach (var item in items)
            {
                var tag = item.SessionId;
                var shownNow = _shown.TryGetValue(tag, out var was) ? was : default;
                if (attention.Views.InView(item.SessionId))
                {
                    _quiet.Add(item.Id);
                    if (shownNow.ItemId is not null)
                    {
                        remove.Add((tag, shownNow.Group));
                        _shown.Remove(tag);
                    }
                    continue;
                }
                if (shownNow.ItemId == item.Id || _quiet.Contains(item.Id) || !Notifies(item.Kind)) continue;
                var n = Build(item);
                show.Add(n);
                _shown[tag] = (item.Id, n.Group);
            }

            foreach (var (tag, (itemId, group)) in _shown.ToList())
            {
                if (current.Contains(itemId)) continue;
                remove.Add((tag, group));
                _shown.Remove(tag);
            }
        }
        foreach (var (tag, group) in remove) notifier.Remove(tag, group);
        foreach (var n in show) notifier.Show(n);
    }

    // An outside session waiting is answered where it runs, which the user is often already looking at.
    private static bool Notifies(AttentionKind kind) => kind != AttentionKind.Waiting;

    private void OnActivated(NotificationAction click)
    {
        string? itemId;
        lock (_gate) itemId = _shown.TryGetValue(click.Tag, out var s) ? s.ItemId : null;
        var item = itemId is null ? null : attention.Board.Get(itemId);
        if (click.Action == "open")
        {
            showTask(item is { TaskId.Length: > 0 } ? item.TaskId : null);
            return;
        }
        if (itemId is null)
        {
            log.Info($"notification click '{click.Action}' for {click.Tag}: nothing current");
            return;
        }
        var refused = attention.Actions.Act(itemId, click.Action, click.Inputs);
        if (refused is null) return;
        log.Info($"notification click '{click.Action}' refused: {refused}");
        // Windows already took the notification down, so say why nothing happened rather than fail silently.
        if (item is not null)
            notifier.Show(new DesktopNotification(click.Tag, GroupOf(item), GroupTitleOf(item), $"{NameOf(item)}: not done", refused, null,
                NotificationLook.Failed, [new NotificationButton("Open", "open")], []));
    }

    private DesktopNotification Build(AttentionItem item)
    {
        var name = NameOf(item);
        var open = new NotificationButton("Open", "open");
        var folder = Path.GetFileName(item.Cwd);
        DesktopNotification N(string title, string body, NotificationLook look, IReadOnlyList<NotificationButton> buttons, IReadOnlyList<NotificationInput>? inputs = null) =>
            new(item.SessionId, GroupOf(item), GroupTitleOf(item), title, body, folder, look, buttons, inputs ?? []);

        switch (item.Kind)
        {
            case AttentionKind.Permission:
                return N($"{name} {item.Title}", item.Detail, NotificationLook.NeedsYou,
                [
                    new NotificationButton("Allow", AttentionActions.Allow, NotificationButtonStyle.Positive),
                    new NotificationButton("Deny", AttentionActions.Deny, NotificationButtonStyle.Negative),
                    open,
                ]);
            case AttentionKind.Question:
                if (QuestionOf(item) is { } q)
                {
                    var choices = q.Options.Select((o, i) => new KeyValuePair<string, string>(i.ToString(System.Globalization.CultureInfo.InvariantCulture), o.Label)).ToList();
                    return N($"{name} {item.Title}", q.Question, NotificationLook.NeedsYou,
                        [new NotificationButton("Answer", AttentionActions.Answer, NotificationButtonStyle.Positive), open],
                        [new NotificationInput(AttentionActions.ChoiceInput, "", choices)]);
                }
                return N($"{name} {item.Title}", item.Detail, NotificationLook.NeedsYou, [open]);
            case AttentionKind.Plan:
                return N($"{name} {item.Title}", item.Detail, NotificationLook.NeedsYou,
                [
                    new NotificationButton("Approve", AttentionActions.Approve, NotificationButtonStyle.Positive),
                    new NotificationButton("Reject", AttentionActions.Reject, NotificationButtonStyle.Negative, AttentionActions.TextInput),
                    open,
                ],
                [new NotificationInput(AttentionActions.TextInput, "What to change (to reject)")]);
            case AttentionKind.Elsewhere:
                return N($"{name} {item.Title}", "Open its thread to see where it's asking.", NotificationLook.NeedsYou, [open]);
            case AttentionKind.Finished:
                return N($"{name} {item.Title}", item.Detail, NotificationLook.Finished,
                    [new NotificationButton("Send", AttentionActions.Send, NotificationButtonStyle.Plain, AttentionActions.TextInput), open],
                    [new NotificationInput(AttentionActions.TextInput, $"Reply to {name}")]);
            case AttentionKind.Failed:
                return N($"{name} {item.Title}", item.Detail, NotificationLook.Failed, [open]);
            default:
                return N($"{name} {item.Title}", item.Detail, NotificationLook.Waiting, [open]);
        }
    }

    private Ochre.Core.Claude.Control.UserQuestion? QuestionOf(AttentionItem item)
    {
        if (sessions.Get(item.SessionId) is not { } session) return null;
        lock (session.Gate)
        {
            return session.Conversation.PendingPermission is { } p && p.Request.RequestId == item.RequestId
                ? AttentionActions.Answerable(p.Request.InputJson)
                : null;
        }
    }

    private static string NameOf(AttentionItem item) => item.Role ?? (Path.GetFileName(item.Cwd) is { Length: > 0 } f ? f : item.SessionId[..Math.Min(8, item.SessionId.Length)]);

    private static string GroupOf(AttentionItem item) => item.TaskId.Length > 0 ? item.TaskId : Outside;

    private string GroupTitleOf(AttentionItem item) =>
        item.TaskId.Length == 0 ? "Not on a task" : tasks.Get(item.TaskId) is { } doc ? $"{item.TaskId} {doc.Title}" : item.TaskId;

    public void Dispose()
    {
        attention.Board.Changed -= Refresh;
        attention.Views.Changed -= Refresh;
        notifier.Activated -= OnActivated;
        notifier.Clear();
    }
}
