using Ochre.Core.Claude;
using Ochre.Core.Tasks;

namespace Ochre.Core.Sessions;

/// <summary>
/// What a session launched from a task is told about Ochre. Without it the agent doesn't know the task exists, so
/// the plan never moves. It travels as <c>--append-system-prompt</c> on the command line, so it is held to
/// <see cref="ClaudeCli.IsSafePrompt"/> (no quotes, apostrophes or semicolons).
/// </summary>
public static class AgentPrompt
{
    /// <summary>The note for <paramref name="taskId"/>, naming the CLI as <paramref name="command"/> (the CLI's file
    /// name without extension, such as <c>ochre</c>). Null when either can't ride the command line safely.</summary>
    public static string? For(string taskId, string command)
    {
        if (!TaskStore.IsTaskId(taskId) || !IsCommandName(command)) return null;
        var note =
            $"You are working on Ochre task {taskId}. Ochre is the task tracker the user runs beside you, and the task holds the plan. " +
            $"Use the {command} command to read and update it: {command} task show prints the title, plan and notes. " +
            $"{command} task check ITEM marks a plan item done (items have ids such as p3). " +
            $"{command} task add-step TEXT adds a plan item. {command} task note TEXT appends to the notes. " +
            "Read the task before you start, tick items as you finish them, and add steps you discover. " +
            "Only the user can mark the task done. " +
            $"When the user parks the task you will be asked for a park note: {command} task park-note TEXT.";
        return ClaudeCli.IsSafePrompt(note) ? note : null;
    }

    /// <summary>The turn that asks a running agent for its park note. Sent as a message, so any text is fine.</summary>
    public static string ParkPrompt(string command) =>
        "The user is parking this task for now. In one or two sentences, record where you are and the very next step, " +
        $"with `{command} task park-note \"<text>\"`. Then stop: don't start new work, the session will be closed and resumed later.";

    /// <summary>
    /// The task commands an agent may run without a permission prompt. The host already confines them to the bound
    /// task and logs every write (<see cref="AgentCommands"/>), and a prompt on every tick would stop agents keeping
    /// the plan current. Everything else they run still asks.
    /// </summary>
    public static IReadOnlyList<string> AllowedTools(string command) =>
        IsCommandName(command) ? [$"Bash({command} task *)", $"PowerShell({command} task *)"] : [];

    private static bool IsCommandName(string command) =>
        command.Length > 0 && command.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
