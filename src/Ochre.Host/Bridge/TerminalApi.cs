using System.Globalization;
using Ochre.Core.Bridge;
using Ochre.Core.Tasks;
using Ochre.Core.Terminals;

namespace Ochre.Host.Bridge;

/// <summary>
/// Terminal messages from the UI. The renderer chooses a shell only by profile id and a folder; it never sends a
/// command line to start. What it types is the user's input, so it's passed on as is (ADR 0011, Consequences).
/// </summary>
internal static class TerminalApi
{
    private const int MaxInput = 200_000;
    private const int MaxCommand = 1000;

    public static void Register(BridgeDispatcher bridge, TerminalManager terminals, TaskStore tasks)
    {
        var j = AppJson.Default;

        bridge.Handle("terminals.profiles", j.Empty, j.IReadOnlyListShellProfileDto, _ =>
            (IReadOnlyList<ShellProfileDto>)terminals.Profiles.Select(p => new ShellProfileDto(p.Id, p.Name)).ToList());

        bridge.Handle("terminals.list", j.Empty, j.IReadOnlyListTerminalDto, _ =>
            (IReadOnlyList<TerminalDto>)terminals.All.Select(Dto).ToList());

        bridge.Handle("terminals.open", j.OpenTerminalRequest, j.TerminalDto, r =>
        {
            var folder = r.Folder.Trim();
            if (folder.Length == 0) folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (folder.Length > 1024 || !Path.IsPathFullyQualified(folder)) throw new BridgeRejectException("choose an absolute folder");
            folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (!Directory.Exists(folder)) throw new BridgeRejectException($"folder not found: {folder}");
            var taskId = CheckTask(r.TaskId, tasks);
            var command = string.IsNullOrWhiteSpace(r.StartupCommand) ? null : r.StartupCommand.Trim();
            if (command is not null && (command.Length > MaxCommand || command.Any(char.IsControl)))
                throw new BridgeRejectException($"a startup command is one line of at most {MaxCommand} characters");
            CheckSize(r.Cols, r.Rows);
            try
            {
                return Dto(terminals.Open(r.Profile, folder, taskId, command, r.Cols, r.Rows));
            }
            catch (ArgumentException)
            {
                throw new BridgeRejectException("unknown shell");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new BridgeRejectException($"the shell could not be started: {ex.Message}");
            }
        });

        bridge.Handle("terminals.attach", j.TerminalIdRequest, j.TerminalSnapshot, r =>
        {
            var t = Find(terminals, r.Id);
            var (text, seq) = terminals.Snapshot(t);
            return new TerminalSnapshot(t.Id, text, seq);
        });

        bridge.Handle("terminals.input", j.TerminalInputRequest, j.Ack, r =>
        {
            if (r.Data.Length > MaxInput) throw new BridgeRejectException("input is too long");
            Refusing(() => terminals.Input(Find(terminals, r.Id), r.Data));
            return new Ack();
        });

        bridge.Handle("terminals.resize", j.TerminalResizeRequest, j.Ack, r =>
        {
            CheckSize(r.Cols, r.Rows);
            terminals.Resize(Find(terminals, r.Id), r.Cols, r.Rows);
            return new Ack();
        });

        bridge.Handle("terminals.restart", j.TerminalIdRequest, j.TerminalDto, r =>
        {
            var t = Find(terminals, r.Id);
            try
            {
                terminals.Restart(t);
            }
            catch (InvalidOperationException ex)
            {
                throw new BridgeRejectException(ex.Message);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new BridgeRejectException($"the shell could not be started: {ex.Message}");
            }
            return Dto(t);
        });

        bridge.Handle("terminals.setTask", j.TerminalSetTaskRequest, j.TerminalDto, r =>
        {
            var t = Find(terminals, r.Id);
            terminals.SetTask(t, CheckTask(r.TaskId, tasks));
            return Dto(t);
        });

        bridge.Handle("terminals.close", j.TerminalIdRequest, j.Ack, r =>
        {
            terminals.Close(Find(terminals, r.Id));
            return new Ack();
        });
    }

    private static string? CheckTask(string? taskId, TaskStore tasks)
    {
        if (string.IsNullOrEmpty(taskId)) return null;
        if (!TaskStore.IsTaskId(taskId)) throw new BridgeRejectException("invalid task id");
        if (tasks.Get(taskId) is null) throw new BridgeRejectException("no such task");
        return taskId;
    }

    private static void CheckSize(int cols, int rows)
    {
        if (cols is < 2 or > 1000 || rows is < 1 or > 500) throw new BridgeRejectException("terminal size out of range");
    }

    private static void Refusing(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            throw new BridgeRejectException(ex.Message);
        }
    }

    private static Terminal Find(TerminalManager terminals, string id) =>
        terminals.Get(id) ?? throw new BridgeRejectException("no such terminal");

    public static TerminalDto Dto(Terminal t) => new(
        t.Id,
        t.Profile.Id,
        t.Profile.Name,
        t.Folder,
        t.TaskId,
        t.Title,
        t.StartupCommand,
        t.Running,
        t.ExitCode,
        t.OpenedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
}
