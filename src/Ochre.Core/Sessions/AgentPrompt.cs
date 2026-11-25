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
        if (!TaskStore.IsTaskId(taskId) || command.Length == 0 || !command.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        var note =
            $"You are working on Ochre task {taskId}. Ochre is the task tracker the user runs beside you, and the task holds the plan. " +
            $"Use the {command} command to read and update it: {command} task show prints the title, plan and notes. " +
            $"{command} task check ITEM marks a plan item done (items have ids such as p3). " +
            $"{command} task add-step TEXT adds a plan item. {command} task note TEXT appends to the notes. " +
            "Read the task before you start, tick items as you finish them, and add steps you discover. " +
            "Only the user can mark the task done.";
        return ClaudeCli.IsSafePrompt(note) ? note : null;
    }
}
