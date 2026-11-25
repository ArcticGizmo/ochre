using System.Text;
using System.Text.Json;
using Ochre.Core.Tasks;

namespace Ochre.Core.Sessions;

/// <summary>One <c>ochre task ...</c> call from an agent, as the CLI sends it.</summary>
/// <param name="SessionId">From the agent's <c>OCHRE_SESSION</c>.</param>
/// <param name="TaskId">From the agent's <c>OCHRE_TASK</c>.</param>
public sealed record AgentRequest(string? SessionId, string? TaskId, string Op, IReadOnlyList<string> Args)
{
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("session", SessionId);
            w.WriteString("task", TaskId);
            w.WriteString("op", Op);
            w.WriteStartArray("args");
            foreach (var a in Args) w.WriteStringValue(a);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Null for anything that isn't a well-formed request.</summary>
    public static AgentRequest? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (Str("op") is not { Length: > 0 } op) return null;
            var args = new List<string>();
            if (root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var item in a.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) return null;
                    args.Add(item.GetString()!);
                }
            return new AgentRequest(Str("session"), Str("task"), op, args);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>An agent asked for something it may not do. The message goes back to the agent.</summary>
public sealed class AgentRefusedException(string message) : Exception(message);

/// <summary>
/// What agents may do to tasks (task-model.md, "Agent surface"; security.md threat 3). An agent reaches only the
/// task its session is bound to, can read it, tick and untick plan items, add steps and append notes. It cannot
/// change the task's state or remove anything, because no command here does. Every write is logged against the
/// session. The binding is checked against the file inside the write, so unbinding a session takes effect at once.
/// </summary>
public sealed class AgentCommands(TaskStore store)
{
    public const int MaxStep = 500;
    public const int MaxNote = 10_000;

    /// <summary>The reply text, or a reply starting with <c>error:</c>.</summary>
    public async Task<string> HandleAsync(AgentRequest? request)
    {
        if (request is null) return "error: malformed request";
        try
        {
            return await RunAsync(request).ConfigureAwait(false);
        }
        catch (AgentRefusedException ex)
        {
            return "error: " + ex.Message;
        }
        catch (KeyNotFoundException)
        {
            return $"error: task {request.TaskId} doesn't exist";
        }
        catch (TaskFormatException ex)
        {
            return $"error: the task file can't be read ({ex.Message}); ask the user to fix it";
        }
    }

    private async Task<string> RunAsync(AgentRequest r)
    {
        if (string.IsNullOrEmpty(r.SessionId)) throw new AgentRefusedException("not running in an Ochre agent session (OCHRE_SESSION is not set)");
        if (string.IsNullOrEmpty(r.TaskId) || !TaskStore.IsTaskId(r.TaskId)) throw new AgentRefusedException("no task for this session (OCHRE_TASK is not set)");
        var taskId = r.TaskId;
        var author = "session " + (r.SessionId.Length > 8 ? r.SessionId[..8] : r.SessionId);

        switch (r.Op, r.Args.Count)
        {
            case ("current", 0):
                RequireBound(store.Get(taskId) ?? throw new KeyNotFoundException(), r.SessionId);
                return taskId;

            case ("show", 0):
            case ("show", 1) when r.Args[0] == "--plan":
            {
                var doc = store.Get(taskId) ?? throw new KeyNotFoundException();
                RequireBound(doc, r.SessionId);
                return Show(taskId, doc, planOnly: r.Args.Count == 1);
            }

            case ("check" or "uncheck", 1):
            {
                var itemId = r.Args[0];
                var done = r.Op == "check";
                await Write(taskId, author, r.SessionId, doc =>
                {
                    if (doc.FindItem(itemId) is null) throw new AgentRefusedException($"no plan item '{itemId}'; run task show to see the ids");
                    return doc.SetItemDone(itemId, done) ? $"{r.Op}ed {itemId}" : null;
                }).ConfigureAwait(false);
                return $"{(done ? "checked" : "unchecked")} {itemId}";
            }

            case ("add-step", 1):
            case ("add-step", 3) when r.Args[1] == "--under":
            {
                var text = Clean(r.Args[0], MaxStep, "step");
                var parent = r.Args.Count == 3 ? r.Args[2] : null;
                string? added = null;
                await Write(taskId, author, r.SessionId, doc =>
                {
                    if (parent is not null && doc.FindItem(parent) is null) throw new AgentRefusedException($"no plan item '{parent}'");
                    added = doc.AddItem(text, parent);
                    return $"added {added}";
                }).ConfigureAwait(false);
                return $"added {added}";
            }

            case ("note", 1):
            {
                var text = Clean(r.Args[0], MaxNote, "note");
                await Write(taskId, author, r.SessionId, doc =>
                {
                    doc.AppendNote(text);
                    return "added a note";
                }).ConfigureAwait(false);
                return "noted";
            }

            default:
                throw new AgentRefusedException($"unknown or malformed command '{r.Op}'. Commands: current, show [--plan], check ITEM, uncheck ITEM, add-step TEXT [--under ITEM], note TEXT");
        }
    }

    private async Task Write(string taskId, string author, string sessionId, Func<TaskDocument, string?> mutate) =>
        await store.UpdateAsync(taskId, author, doc =>
        {
            RequireBound(doc, sessionId);
            return mutate(doc);
        }).ConfigureAwait(false);

    private static void RequireBound(TaskDocument doc, string sessionId)
    {
        if (!doc.Sessions.Any(s => string.Equals(s.Id, sessionId, StringComparison.Ordinal)))
            throw new AgentRefusedException($"this session isn't bound to {doc.Id}; an agent can only use the task it was started from");
    }

    private static string Clean(string value, int max, string what)
    {
        var t = value.Trim();
        if (t.Length == 0) throw new AgentRefusedException($"the {what} is empty");
        if (t.Length > max) throw new AgentRefusedException($"the {what} is longer than {max} characters");
        return t;
    }

    private static string Show(string id, TaskDocument doc, bool planOnly)
    {
        var sb = new StringBuilder();
        if (!planOnly)
        {
            sb.Append(id).Append("  ").Append(doc.Title).Append("  [").Append(doc.State.ToString().ToLowerInvariant()).Append("]\n");
            if (doc.Park is { } park) sb.Append("\nPark note:\n").Append(park.Note.Trim()).Append('\n');
            sb.Append("\nPlan:\n");
        }
        var items = doc.Plan;
        if (items.Count == 0 && doc.PlanText.Length == 0) sb.Append("  (no plan yet; add steps with task add-step)\n");
        if (doc.PlanText.Length > 0) sb.Append(doc.PlanText.TrimEnd()).Append('\n');
        foreach (var item in items) AppendItem(sb, item, 1);
        if (!planOnly)
        {
            sb.Append("\nNotes:\n").Append(doc.Notes.Length > 0 ? doc.Notes.Trim() : "(none)").Append('\n');
            if (doc.Repos.Count > 0) sb.Append("\nRepos:\n").Append(string.Join('\n', doc.Repos.Select(r => "  " + r))).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppendItem(StringBuilder sb, PlanItem item, int depth)
    {
        sb.Append(' ', depth * 2).Append(item.Done ? "[x] " : "[ ] ").Append(item.Id ?? "?").Append("  ").Append(item.Text).Append('\n');
        foreach (var child in item.Children) AppendItem(sb, child, depth + 1);
    }
}
