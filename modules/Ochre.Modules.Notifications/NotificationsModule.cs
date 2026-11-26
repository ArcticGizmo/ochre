using Ochre.Core.Bridge;
using Ochre.Core.Modules;
using Ochre.Core.Platform;

namespace Ochre.Modules.Notifications;

/// <summary>Desktop notifications for the attention inbox (M5 slice 4, ADR 0013), answered from the notification.</summary>
internal sealed class NotificationsModule : IModule
{
    public string Id => "notifications";
    public string Name => "Notifications";
    public string Description => "Windows notifications when an agent needs you or finishes, grouped by task, with Allow, Deny, answers and replies right on the notification.";
    public bool DefaultEnabled => true;

    public IReadOnlyList<RequestContract> Requests { get; } = [];

    public IReadOnlyList<EventContract> Events { get; } = [];

    public IDisposable Start(IModuleContext context)
    {
        var sync = new NotificationSync(context.Service<IDesktopNotifier>(), context.Attention, context.Sessions, context.Tasks, context.ShowTask, context.Log);
        sync.Start();
        return sync;
    }
}
