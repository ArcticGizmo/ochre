using Ochre.Core.Bridge;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;

namespace Ochre.Host.Bridge;

/// <summary>
/// Task messages from the UI. The UI acts as the user, so it may do what agents may not (change state, edit
/// notes); every input is still validated here because the renderer is untrusted.
/// </summary>
internal static class TaskApi
{
    private const string UserAuthor = "user";
    private const int MaxTitle = 200;
    private const int MaxItemText = 500;
    private const int MaxNotes = 100_000;
    private const int MaxRepos = 20;

    public static void Register(BridgeDispatcher bridge, TaskStore store, ParkCoordinator parking)
    {
        var j = AppJson.Default;
        TaskDetail Detail(string id, TaskDocument doc) => ToDetail(id, doc, parking.WaitingFor(id));

        bridge.Handle("tasks.park", j.ParkTaskRequest, j.Ack, async r =>
        {
            if (r.Note is { Length: > MaxNotes }) throw new BridgeRejectException("note too long");
            var doc = store.Get(ValidId(r.Id)) ?? throw new BridgeRejectException("no such task");
            if (doc.State is TaskState.Parked or TaskState.Done) throw new BridgeRejectException($"the task is already {doc.State.ToString().ToLowerInvariant()}");
            try
            {
                // Returns once the task is Parked; collecting the agents' notes carries on and shows as it lands.
                _ = await parking.ParkAsync(r.Id, r.Note).ConfigureAwait(true);
            }
            catch (TaskFormatException ex)
            {
                throw new BridgeRejectException($"the task file can't be read ({ex.Message}); fix it in an editor and try again");
            }
            return new Ack();
        });

        bridge.Handle("tasks.list", j.Empty, j.IReadOnlyListTaskListItem, _ =>
            (IReadOnlyList<TaskListItem>)store.List().Select(ToListItem).ToList());

        bridge.Handle("tasks.get", j.TaskIdRequest, j.TaskDetail, r =>
            Detail(r.Id, store.Get(ValidId(r.Id)) ?? throw new BridgeRejectException("no such task")));

        bridge.Handle("tasks.create", j.CreateTaskRequest, j.TaskDetail, async r =>
        {
            var title = Text(r.Title, MaxTitle, "title");
            if (r.Notes is { Length: > MaxNotes }) throw new BridgeRejectException("description too long");
            var repos = r.Repos is null ? [] : ValidRepos(r.Repos);
            var id = await store.CreateAsync(title, UserAuthor, doc =>
            {
                if (!string.IsNullOrWhiteSpace(r.Notes)) doc.SetNotes(r.Notes);
                if (repos.Count > 0) doc.SetRepos(repos);
            }).ConfigureAwait(true);
            return Detail(id, store.Get(id)!);
        });

        bridge.Handle("tasks.delete", j.TaskIdRequest, j.Ack, async r =>
        {
            try
            {
                await store.DeleteAsync(ValidId(r.Id), UserAuthor).ConfigureAwait(true);
            }
            catch (KeyNotFoundException)
            {
                throw new BridgeRejectException("no such task");
            }
            return new Ack();
        });

        bridge.Handle("tasks.update", j.UpdateTaskRequest, j.TaskDetail, r => Update(store, Detail, r.Id, doc =>
        {
            var changes = new List<string>();
            if (r.Title is not null && Text(r.Title, MaxTitle, "title") is var title && title != doc.Title)
            {
                doc.Title = title;
                changes.Add("renamed");
            }
            if (r.Notes is { } notes)
            {
                if (notes.Length > MaxNotes) throw new BridgeRejectException("notes too long");
                if (notes.Trim() != doc.Notes)
                {
                    doc.SetNotes(notes);
                    changes.Add("edited notes");
                }
            }
            if (r.Repos is { } repos)
            {
                var clean = ValidRepos(repos);
                if (!clean.SequenceEqual(doc.Repos, StringComparer.Ordinal))
                {
                    doc.SetRepos(clean);
                    changes.Add("set repos");
                }
            }
            return changes.Count == 0 ? null : string.Join(", ", changes);
        }));

        bridge.Handle("tasks.setState", j.SetStateRequest, j.TaskDetail, r => Update(store, Detail, r.Id, doc =>
        {
            var state = FromDto(r.State);
            if (doc.State == state) return null;
            doc.State = state;
            return $"set state {state.ToString().ToLowerInvariant()}";
        }));

        bridge.Handle("plan.check", j.CheckItemRequest, j.TaskDetail, r => Update(store, Detail, r.Id, doc =>
        {
            if (doc.FindItem(r.ItemId) is null) throw new BridgeRejectException("no such plan item");
            return doc.SetItemDone(r.ItemId, r.Done) ? $"{(r.Done ? "checked" : "unchecked")} {r.ItemId}" : null;
        }));

        bridge.Handle("tasks.reviewAgentChanges", j.TaskIdRequest, j.TaskDetail, r => Update(store, Detail, r.Id, doc =>
        {
            var pending = AgentChanges.Unreviewed(doc);
            return pending.PlanItems.Count == 0 && !pending.Notes ? null : AgentChanges.ReviewedMessage;
        }));

        bridge.Handle("plan.add", j.AddItemRequest, j.TaskDetail, r => Update(store, Detail, r.Id, doc =>
        {
            var text = Text(r.Text, MaxItemText, "step");
            if (r.ParentId is { } parent && doc.FindItem(parent) is null) throw new BridgeRejectException("no such plan item");
            return $"added {doc.AddItem(text, r.ParentId)}";
        }));
    }

    private static async ValueTask<TaskDetail> Update(TaskStore store, Func<string, TaskDocument, TaskDetail> detail, string id, Func<TaskDocument, string?> mutate)
    {
        try
        {
            var doc = await store.UpdateAsync(ValidId(id), UserAuthor, mutate).ConfigureAwait(true);
            return detail(id, doc);
        }
        catch (KeyNotFoundException)
        {
            throw new BridgeRejectException("no such task");
        }
        catch (TaskFormatException ex)
        {
            throw new BridgeRejectException($"the task file can't be read ({ex.Message}); fix it in an editor and try again");
        }
    }

    private static string ValidId(string id) =>
        TaskStore.IsTaskId(id) ? id : throw new BridgeRejectException("invalid task id");

    private static string Text(string value, int max, string what)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) throw new BridgeRejectException($"{what} is empty");
        if (trimmed.Length > max) throw new BridgeRejectException($"{what} is longer than {max} characters");
        return trimmed;
    }

    private static List<string> ValidRepos(IReadOnlyList<string> repos)
    {
        if (repos.Count > MaxRepos) throw new BridgeRejectException($"at most {MaxRepos} repos");
        var clean = new List<string>();
        foreach (var repo in repos)
        {
            var path = repo.Trim();
            if (path.Length == 0) continue;
            if (path.Length > 1024 || !Path.IsPathFullyQualified(path)) throw new BridgeRejectException($"not an absolute path: {path}");
            if (!clean.Contains(path, StringComparer.OrdinalIgnoreCase)) clean.Add(path);
        }
        return clean;
    }

    private static TaskState FromDto(TaskStateDto s) => s switch
    {
        TaskStateDto.Active => TaskState.Active,
        TaskStateDto.Parked => TaskState.Parked,
        TaskStateDto.Done => TaskState.Done,
        _ => TaskState.Backlog,
    };

    private static TaskStateDto ToDto(TaskState s) => s switch
    {
        TaskState.Active => TaskStateDto.Active,
        TaskState.Parked => TaskStateDto.Parked,
        TaskState.Done => TaskStateDto.Done,
        _ => TaskStateDto.Backlog,
    };

    private static string? Iso(DateTimeOffset? t) => t?.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static TaskListItem ToListItem(TaskSummary s) =>
        new(s.Id, s.Title, ToDto(s.State), Iso(s.Updated), s.PlanDone, s.PlanTotal, s.NextStep, s.ParkNote, Iso(s.ParkedAt), s.Links.Select(ToDto).ToList(), s.Sessions.Select(ToDto).ToList());

    private static LinkDto ToDto(TaskLink l) => new(l.Id, l.Kind, l.Ref, l.Title, l.Summary);

    private static SessionRefDto ToDto(TaskSessionRef s) => new(s.Id, s.Cwd, s.Binding, s.Role);

    private static TaskDetail ToDetail(string id, TaskDocument doc, IReadOnlyList<string> parkingWaitingFor)
    {
        var unreviewed = AgentChanges.Unreviewed(doc);
        return new(
            id,
            doc.Title,
            ToDto(doc.State),
            Iso(doc.Created),
            Iso(doc.Updated),
            doc.Repos,
            doc.Links.Select(ToDto).ToList(),
            doc.Sessions.Select(ToDto).ToList(),
            doc.Park is { } p ? new ParkDto(Iso(p.At)!, p.Note) : null,
            doc.Plan.Select(ToDto).ToList(),
            doc.PlanText,
            doc.Notes,
            doc.Log.Select(l => l.Text).ToList(),
            unreviewed.PlanItems,
            unreviewed.Notes,
            parkingWaitingFor);
    }

    private static PlanItemDto ToDto(PlanItem item) =>
        new(item.Id ?? "", item.Text, item.Done, item.Children.Select(ToDto).ToList());
}
