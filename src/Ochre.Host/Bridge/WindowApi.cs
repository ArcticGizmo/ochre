using Ochre.Core.Bridge;
using Ochre.Core.Hosting;

namespace Ochre.Host.Bridge;

/// <summary>Per-window messages: each page's bridge is bound to the window it's in.</summary>
internal static class WindowApi
{
    public static void Register(BridgeDispatcher bridge, OchreHost host, IShellWindow window)
    {
        var j = AppJson.Default;
        bridge.Handle("window.get", j.Empty, j.WindowInfo, _ =>
            new WindowInfo(window.InitialTask is { } t && host.Tasks.Get(t) is not null ? t : null));
        bridge.Handle("window.setTask", j.WindowTaskRequest, j.Ack, r =>
        {
            // Saved to windows.json and matched against later opens, so only ever a task that exists.
            if (r.Task is not null && (r.Task.Length > 64 || host.Tasks.Get(r.Task) is null)) throw new BridgeRejectException("no such task");
            window.ReportTask(r.Task);
            return new Ack();
        });
        bridge.Handle("windows.openTask", j.OpenTaskRequest, j.OpenTaskResult, async ValueTask<OpenTaskResult> (r) =>
        {
            if (r.Id.Length > 64 || host.Tasks.Get(r.Id) is null) throw new BridgeRejectException("no such task");
            if (host.Shell is not { } shell) throw new BridgeRejectException("Ochre is starting up");
            var outcome = await shell.OpenTaskAsync(r.Id, Choice(r.Choice), window).ConfigureAwait(false);
            return new OpenTaskResult(Outcome(outcome));
        });
    }

    public static OpenTaskChoice Choice(OpenTaskChoiceDto c) => c switch
    {
        OpenTaskChoiceDto.Bring => OpenTaskChoice.Bring,
        OpenTaskChoiceDto.New => OpenTaskChoice.New,
        _ => OpenTaskChoice.Ask,
    };

    public static OpenTaskOutcomeDto Outcome(OpenTaskOutcome o) => o switch
    {
        OpenTaskOutcome.Focused => OpenTaskOutcomeDto.Focused,
        OpenTaskOutcome.Moved => OpenTaskOutcomeDto.Moved,
        OpenTaskOutcome.Elsewhere => OpenTaskOutcomeDto.Elsewhere,
        _ => OpenTaskOutcomeDto.Opened,
    };
}
