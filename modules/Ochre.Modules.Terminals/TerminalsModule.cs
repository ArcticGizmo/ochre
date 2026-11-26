using Ochre.Core.Bridge;
using Ochre.Core.Modules;
using Ochre.Core.Platform;
using Ochre.Modules.Terminals.Bridge;

namespace Ochre.Modules.Terminals;

/// <summary>Terminal tabs (M3, ADR 0011): shells in pseudoconsoles, kept by the host and restored after a quit.</summary>
internal sealed class TerminalsModule : IModule
{
    public string Id => "terminals";
    public string Name => "Terminals";
    public string Description => "Terminal tabs that keep running with every window closed, can belong to a task, and come back with their scrollback after Ochre restarts.";
    public bool DefaultEnabled => true;

    public IReadOnlyList<RequestContract> Requests { get; } =
    [
        new("terminals.profiles", typeof(Empty), typeof(IReadOnlyList<ShellProfileDto>)),
        new("terminals.list", typeof(Empty), typeof(IReadOnlyList<TerminalDto>)),
        new("terminals.open", typeof(OpenTerminalRequest), typeof(TerminalDto)),
        new("terminals.attach", typeof(TerminalIdRequest), typeof(TerminalSnapshot)),
        new("terminals.input", typeof(TerminalInputRequest), typeof(Ack)),
        new("terminals.resize", typeof(TerminalResizeRequest), typeof(Ack)),
        new("terminals.restart", typeof(TerminalIdRequest), typeof(TerminalDto)),
        new("terminals.setTask", typeof(TerminalSetTaskRequest), typeof(TerminalDto)),
        new("terminals.setKeepScrollback", typeof(TerminalKeepScrollbackRequest), typeof(TerminalDto)),
        new("terminals.rerun", typeof(TerminalIdRequest), typeof(Ack)),
        new("terminals.dismissRestore", typeof(TerminalIdRequest), typeof(Ack)),
        new("terminals.close", typeof(TerminalIdRequest), typeof(Ack)),
    ];

    public IReadOnlyList<EventContract> Events { get; } =
    [
        // A chunk of a terminal's output, numbered so a window that attached mid-stream skips what its snapshot holds.
        new("terminals.output", typeof(TerminalOutputEvent)),
        // A terminal opened, closed, exited, or changed title, folder or task: fetch terminals.list again.
        new("terminals.changed", typeof(Empty)),
    ];

    public IDisposable Start(IModuleContext context)
    {
        var terminals = new TerminalManager(context.Service<ITerminalPlatform>(), context.Log);
        terminals.Output += (t, data, seq) => context.Broadcast("terminals.output", new TerminalOutputEvent(t.Id, data, seq), TerminalsJson.Default.TerminalOutputEvent);
        terminals.Changed += () => context.Broadcast("terminals.changed", new Empty(), TerminalsJson.Default.Empty);

        var saver = new TerminalPersistence(terminals, new TerminalStore(context.DataFolder, context.Service<IDataProtector>()), context.Log);
        var restored = saver.RestoreAll();
        if (restored > 0) context.Log.Info($"restored {restored} terminal tab(s)");

        context.AddBridge(bridge => TerminalApi.Register(bridge, terminals, context.Tasks));
        // Windows may end the process before the normal quit path runs.
        context.OnSessionEnding(saver.SaveNow);
        return new Stopper(terminals, saver);
    }

    private sealed class Stopper(TerminalManager terminals, TerminalPersistence saver) : IDisposable
    {
        public void Dispose()
        {
            // Saved before the shells close, so the scrollback includes everything up to the quit.
            saver.SaveNow();
            saver.Dispose();
            terminals.Dispose();
        }
    }
}
