using Ochre.Core.Bridge;
using Ochre.Core.Claude;
using Ochre.Core.Claude.Control;
using Ochre.Core.Sessions;
using Ochre.Core.Tasks;

namespace Ochre.Host.Bridge;

/// <summary>
/// Session messages from the UI: start an agent on a task, read its thread, send to it, answer its prompts. The
/// renderer is untrusted, so every path, mode and length is checked here before it reaches a process.
/// </summary>
internal static partial class SessionApi
{
    private const int MaxPrompt = 100_000;
    private const int MaxRole = 40;
    private const int MaxAnswers = 20;
    // Tool inputs and diffs can be whole files; the thread shows enough to recognise the change.
    private const int MaxInputChars = 20_000;
    private const int MaxDiffLines = 400;

    private static readonly HashSet<string> PermissionModes = new(StringComparer.Ordinal) { "default", "acceptEdits", "plan", "auto" };

    public static void Register(BridgeDispatcher bridge, SessionManager sessions)
    {
        var j = AppJson.Default;

        bridge.Handle("sessions.list", j.Empty, j.IReadOnlyListSessionSummary, _ =>
            (IReadOnlyList<SessionSummary>)sessions.All.Select(s => Summary(s, sessions.Suggest(s))).ToList());

        bridge.Handle("sessions.bind", j.BindSessionRequest, j.Ack, async r =>
        {
            if (!TaskStore.IsTaskId(r.TaskId)) throw new BridgeRejectException("invalid task id");
            try
            {
                await sessions.BindAsync(Find(sessions, r.Id), r.TaskId).ConfigureAwait(true);
            }
            catch (InvalidOperationException ex)
            {
                throw new BridgeRejectException(ex.Message);
            }
            catch (KeyNotFoundException)
            {
                throw new BridgeRejectException("no such task");
            }
            return new Ack();
        });

        bridge.Handle("sessions.unbind", j.SessionIdRequest, j.Ack, async r =>
        {
            try
            {
                await sessions.UnbindAsync(Find(sessions, r.Id)).ConfigureAwait(true);
            }
            catch (InvalidOperationException ex)
            {
                throw new BridgeRejectException(ex.Message);
            }
            catch (KeyNotFoundException)
            {
                throw new BridgeRejectException("its task no longer exists");
            }
            return new Ack();
        });

        bridge.Handle("sessions.get", j.SessionIdRequest, j.SessionSnapshot, async r =>
        {
            var s = Find(sessions, r.Id);
            await sessions.EnsureHistoryAsync(s).ConfigureAwait(true);
            lock (s.Gate)
            {
                var items = s.Conversation.Items;
                return new SessionSnapshot(Header(s), s.Conversation.TurnActive, items.Select((item, i) => Item(i, item)).ToList());
            }
        });

        bridge.Handle("sessions.start", j.StartSessionRequest, j.StartSessionResult, async r =>
        {
            if (!TaskStore.IsTaskId(r.TaskId)) throw new BridgeRejectException("invalid task id");
            var cwd = r.Cwd.Trim();
            if (cwd.Length == 0 || cwd.Length > 1024 || !Path.IsPathFullyQualified(cwd)) throw new BridgeRejectException("choose an absolute folder");
            if (!Directory.Exists(cwd)) throw new BridgeRejectException($"folder not found: {cwd}");
            var role = string.IsNullOrWhiteSpace(r.Role) ? null : r.Role.Trim();
            if (role is not null && (role.Length > MaxRole || !role.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')))
                throw new BridgeRejectException($"a role is at most {MaxRole} letters, digits, spaces or dashes");
            var mode = string.IsNullOrEmpty(r.PermissionMode) ? null : r.PermissionMode;
            if (mode is not null && !PermissionModes.Contains(mode)) throw new BridgeRejectException("unknown permission mode");
            if (r.FirstPrompt is { Length: > MaxPrompt }) throw new BridgeRejectException("the first message is too long");

            try
            {
                var (session, refusal) = await sessions.StartAsync(r.TaskId, cwd, role, mode, r.FirstPrompt, r.TrustFolder).ConfigureAwait(true);
                return new StartSessionResult(session?.Id, refusal == StartRefusal.NeedsTrust);
            }
            catch (KeyNotFoundException)
            {
                throw new BridgeRejectException("no such task");
            }
            catch (FileNotFoundException ex)
            {
                throw new BridgeRejectException(ex.Message);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new BridgeRejectException($"claude could not be started: {ex.Message}");
            }
        });

        bridge.Handle("sessions.send", j.SendPromptRequest, j.Ack, r =>
        {
            var text = r.Text.Trim();
            if (text.Length == 0) throw new BridgeRejectException("message is empty");
            if (text.Length > MaxPrompt) throw new BridgeRejectException("message is too long");
            try
            {
                sessions.Send(Find(sessions, r.Id), text);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
            {
                throw new BridgeRejectException(ex.Message);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new BridgeRejectException($"claude could not be started: {ex.Message}");
            }
            return new Ack();
        });

        bridge.Handle("sessions.interrupt", j.SessionIdRequest, j.Ack, r =>
        {
            Refusing(() => sessions.Interrupt(Find(sessions, r.Id)));
            return new Ack();
        });

        bridge.Handle("sessions.stop", j.SessionIdRequest, j.Ack, r =>
        {
            Refusing(() => sessions.Stop(Find(sessions, r.Id)));
            return new Ack();
        });

        bridge.Handle("sessions.respond", j.RespondRequest, j.Ack, r =>
        {
            if (r.SwitchMode is { } m && !PermissionModes.Contains(m)) throw new BridgeRejectException("unknown permission mode");
            Dictionary<string, IReadOnlyList<string>>? answers = null;
            if (r.Answers is { } given)
            {
                if (given.Count > MaxAnswers || given.Any(a => a.Labels.Count > MaxAnswers || a.Question.Length > 2000 || a.Labels.Any(l => l.Length > 2000)))
                    throw new BridgeRejectException("too many answers");
                answers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var a in given) answers[a.Question] = a.Labels;
            }
            if (!sessions.Respond(Find(sessions, r.Id), r.RequestId, r.Allow, r.SwitchMode, answers))
                throw new BridgeRejectException("that prompt is no longer waiting for an answer");
            return new Ack();
        });

        bridge.Handle("sessions.setModel", j.SetModelRequest, j.Ack, r =>
        {
            if (!ModelId().IsMatch(r.Model)) throw new BridgeRejectException("not a model id");
            Refusing(() => sessions.SetModel(Find(sessions, r.Id), r.Model));
            return new Ack();
        });

        bridge.Handle("sessions.setEffort", j.SetEffortRequest, j.Ack, r =>
        {
            if (r.Effort != "auto" && !ClaudeSessionController.IsEffortLevel(r.Effort)) throw new BridgeRejectException("unknown effort level");
            Refusing(() => sessions.SetEffort(Find(sessions, r.Id), r.Effort));
            return new Ack();
        });
    }

    // The manager refuses with InvalidOperationException (e.g. a read-only observed session); the UI gets the reason.
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

    // Ids like claude-opus-5-5 or aliases like opus; it rides the command line on resume, so nothing else gets through.
    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-z][a-z0-9.\-]{0,63}$")]
    private static partial System.Text.RegularExpressions.Regex ModelId();

    /// <summary>The broadcast for a flushed batch of changes. Reads the items under the session's gate.</summary>
    public static SessionUpdateEvent Update(LiveSession s, SessionChanges changes)
    {
        lock (s.Gate)
        {
            var items = s.Conversation.Items;
            var dtos = changes.Reset ? [] : changes.Items.Where(i => i < items.Count).Select(i => Item(i, items[i])).ToList();
            return new SessionUpdateEvent(s.Id, changes.Reset, Header(s), s.Conversation.TurnActive, dtos);
        }
    }

    public static SessionSummary Summary(LiveSession s, IReadOnlyList<BindSuggestion> suggestions)
    {
        lock (s.Gate)
            return new SessionSummary(s.Id, s.TaskId, s.Cwd, s.Role, Status(s), s.StartedAt.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                Activity(s), Subagents(s), s.Observed, s.Branch, suggestions.Select(b => new BindSuggestionDto(b.TaskId, b.Reason)).ToList());
    }

    private static LiveSession Find(SessionManager sessions, string id) =>
        sessions.Get(id) ?? throw new BridgeRejectException("no such session");

    // Callers hold the session's gate.
    private static SessionHeader Header(LiveSession s)
    {
        var c = s.Conversation;
        return new SessionHeader(s.Id, s.TaskId, s.Cwd, s.Role, Status(s), s.Model ?? c.Model, s.Effort ?? "auto", c.PermissionMode, c.TotalCostUsd,
            c.ContextTokens, c.QueuedPrompts, Activity(s), Subagents(s), s.Observed, s.Branch);
    }

    private static bool IsAgentTool(string name) => name is "Agent" or "Task";

    // Callers hold the session's gate. The latest assistant message is where the current turn's tools are.
    private static IReadOnlyList<AssistantPart> CurrentParts(SessionConversation c) =>
        c.TurnActive && c.Items.LastOrDefault(i => i is AssistantMessageItem) is AssistantMessageItem a ? a.Parts : [];

    private static string? Activity(LiveSession s)
    {
        var c = s.Conversation;
        if (s.Dormant || !c.TurnActive) return null;
        if (c.PendingPermission is { } p) return $"asking to use {p.Request.ToolName}";
        var parts = CurrentParts(c);
        if (parts.OfType<ToolCallPart>().LastOrDefault(t => t.Status == ToolCallStatus.Running && !IsAgentTool(t.ToolName)) is { } tool) return tool.Summary;
        return parts.LastOrDefault() switch
        {
            ThinkingPart => "thinking",
            TextPart => "writing",
            ToolCallPart t when IsAgentTool(t.ToolName) && t.Status == ToolCallStatus.Running => "waiting on a subagent",
            _ => "working",
        };
    }

    private static IReadOnlyList<SubagentDto> Subagents(LiveSession s)
    {
        if (s.Dormant) return [];
        var c = s.Conversation;
        var foreground = CurrentParts(c).OfType<ToolCallPart>()
            .Where(t => IsAgentTool(t.ToolName) && t.Status == ToolCallStatus.Running && t.BackgroundTaskId is null)
            .Select(t => new SubagentDto(t.Input?["description"]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? t.Input["description"]!.GetValue<string>() : t.Summary, null));
        var background = c.BackgroundTasks.Running
            .Where(t => t.Kind == BackgroundTaskKind.Agent)
            .Select(t => new SubagentDto(t.Description, t.Progress));
        return [.. foreground, .. background];
    }

    private static SessionStatusDto Status(LiveSession s) =>
        s.Observed ? s.ObservedStatus switch
        {
            "busy" => SessionStatusDto.Busy,
            "waiting" => SessionStatusDto.Waiting,
            "idle" => SessionStatusDto.Idle,
            _ => SessionStatusDto.Dormant,
        }
        : s.Dormant ? SessionStatusDto.Dormant
        : s.Conversation.PendingPermission is not null || s.Conversation.WaitingElsewhere ? SessionStatusDto.Waiting
        : s.Conversation.TurnActive ? SessionStatusDto.Busy
        : SessionStatusDto.Idle;

    private static SessionItemDto Item(int index, ConversationItem item) => item switch
    {
        UserMessageItem u => new SessionItemDto(index, ItemKindDto.User, u.Text, false, true, null, null),
        NoteItem n => new SessionItemDto(index, ItemKindDto.Note, n.Text, n.Kind == NoteKind.Error, true, null, null),
        AssistantMessageItem a => new SessionItemDto(index, ItemKindDto.Assistant, "", false, a.IsComplete, a.Parts.Select(Part).ToList(), null),
        PermissionItem p => new SessionItemDto(index, ItemKindDto.Permission, "", false, true, null, Permission(p)),
        CompactionItem c => new SessionItemDto(index, ItemKindDto.Note, CompactionText(c), c.Failed, true, null, null),
        TaskNoticeItem t => new SessionItemDto(index, ItemKindDto.Note, NoticeText(t), t.Task.Status == BackgroundTaskStatus.Failed, true, null, null),
        _ => new SessionItemDto(index, ItemKindDto.Note, "", false, true, null, null),
    };

    private static AssistantPartDto Part(AssistantPart part) => part switch
    {
        TextPart t => new AssistantPartDto(PartKindDto.Text, t.Text, t.IsStreaming, null, null, null, null, null),
        ThinkingPart t => new AssistantPartDto(PartKindDto.Thinking, t.Text, false, null, null, null, null, null),
        ToolCallPart t => new AssistantPartDto(PartKindDto.Tool, t.Summary, false, t.ToolName, Clip(t.InputJson, MaxInputChars),
            t.Status switch
            {
                ToolCallStatus.Done => ToolStatusDto.Done,
                ToolCallStatus.Failed => ToolStatusDto.Failed,
                _ => ToolStatusDto.Running,
            },
            t.ResultText, Diff(t)),
        _ => new AssistantPartDto(PartKindDto.Text, "", false, null, null, null, null, null),
    };

    private static IReadOnlyList<string>? Diff(ToolCallPart t)
    {
        if (t.Diff is not { Count: > 0 } diff) return null;
        var lines = diff.Take(MaxDiffLines).Select(l => l.Kind switch
        {
            GitDiffLineKind.Added => "+" + l.Text,
            GitDiffLineKind.Removed => "-" + l.Text,
            GitDiffLineKind.Meta => "@" + l.Text,
            _ => " " + l.Text,
        }).ToList();
        if (diff.Count > MaxDiffLines) lines.Add($"@… {diff.Count - MaxDiffLines} more lines");
        return lines;
    }

    private static PermissionDto Permission(PermissionItem p) => new(
        p.Request.RequestId,
        p.Request.ToolName,
        p.Request.Description,
        Clip(p.Request.InputJson, MaxInputChars),
        p.Request.SuggestedMode,
        p.Resolution switch
        {
            PermissionResolution.Allowed => ResolutionDto.Allowed,
            PermissionResolution.Denied => ResolutionDto.Denied,
            PermissionResolution.Expired => ResolutionDto.Expired,
            PermissionResolution.AnsweredElsewhere => ResolutionDto.AnsweredElsewhere,
            _ => ResolutionDto.Pending,
        },
        p.IsQuestion
            ? AskUserQuestionInput.Parse(p.Request.InputJson)
                .Select(q => new QuestionDto(q.Question, q.Header, q.Options.Select(o => new QuestionOptionDto(o.Label, o.Description)).ToList(), q.MultiSelect))
                .ToList()
            : null,
        p.IsPlan ? PlanApprovalInput.Parse(p.Request.InputJson) : null,
        p.AnswerSummary,
        p.SwitchedMode);

    private static string CompactionText(CompactionItem c) =>
        !c.IsDone ? $"compacting the conversation{(c.Percent is { } pct ? $" ({pct}%)" : "")}"
        : c.Failed ? "compaction stopped; nothing was freed"
        : c.FreedTokens > 0 ? $"conversation compacted, freed {c.FreedTokens:N0} tokens"
        : "conversation compacted";

    private static string NoticeText(TaskNoticeItem t)
    {
        var name = t.Task.Description.Length > 0 ? t.Task.Description : t.Task.TaskId;
        if (t.IsEvent) return $"{name}: {t.EventText}";
        var status = t.Task.Status.ToString().ToLowerInvariant();
        return t.Task.ExitCode is { } code ? $"{name} {status} (exit {code})" : $"{name} {status}";
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
