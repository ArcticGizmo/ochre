using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ochre.Core.Claude;

namespace Ochre.Core.Sessions;

/// <param name="Id">The id the session was launched with; the key the task knows it by.</param>
/// <param name="ClaudeId">The Claude session to resume: the launch id until a <c>/clear</c> moves it to a new one.</param>
/// <param name="Model">The model the user picked for it, so a resume keeps it; null is Claude's default.</param>
/// <param name="Effort">The effort level the user picked; null is Claude's default ("auto").</param>
public sealed record LedgerEntry(
    string Id,
    string TaskId,
    string Cwd,
    string? ConfigDir,
    string? Role,
    string? PermissionMode,
    string ClaudeId,
    DateTimeOffset StartedAt,
    string? Model = null,
    string? Effort = null);

/// <summary>A task a shutdown parked, with what it had before so a cancelled shutdown can put it back.</summary>
/// <param name="AutoNote">The note the shutdown wrote; the task is only put back while it still has this note.</param>
public sealed record ShutdownParkedTask(string TaskId, string PreviousState, DateTimeOffset? PreviousParkAt, string? PreviousParkNote, string AutoNote);

/// <summary>When Windows said it was ending the session, and the tasks Ochre parked because of it.</summary>
public sealed record ShutdownStamp(DateTimeOffset At, IReadOnlyList<ShutdownParkedTask> Tasks);

/// <summary>
/// What the host needs to bring a launched session back after a restart that the task file doesn't hold: how it was
/// launched and which Claude session it has become. Modelled on Perch's <c>SessionLedger</c>; one small JSON file in
/// the local app data folder, replaced atomically, never holding prompts or output. The task's <c>sessions</c> list
/// stays the record of which sessions belong to it; an entry here without one is ignored.
/// </summary>
public sealed class SessionLedger(string path)
{
    private readonly Lock _gate = new();
    private Dictionary<string, LedgerEntry>? _entries;
    private ShutdownStamp? _shutdown;

    /// <summary>The last shutdown's stamp, until it's cleared (cancelled, or seen at the next start).</summary>
    public ShutdownStamp? Shutdown
    {
        get
        {
            lock (_gate)
            {
                Entries();
                return _shutdown;
            }
        }
        set
        {
            lock (_gate)
            {
                Entries();
                _shutdown = value;
                Save();
            }
        }
    }

    public LedgerEntry? Get(string id)
    {
        lock (_gate) return Entries().GetValueOrDefault(id);
    }

    public void Upsert(LedgerEntry entry)
    {
        lock (_gate)
        {
            Entries()[entry.Id] = entry;
            Save();
        }
    }

    /// <summary>The user picked a model or effort for the session; later resumes launch with them.</summary>
    public void SetChoices(string id, string? model, string? effort)
    {
        lock (_gate)
        {
            if (!Entries().TryGetValue(id, out var e) || (e.Model == model && e.Effort == effort)) return;
            Entries()[id] = e with { Model = model, Effort = effort };
            Save();
        }
    }

    /// <summary>A <c>/clear</c> gave the session a new Claude id; resume that one from now on.</summary>
    public void Rebind(string id, string claudeId)
    {
        lock (_gate)
        {
            if (!Entries().TryGetValue(id, out var e) || e.ClaudeId == claudeId) return;
            Entries()[id] = e with { ClaudeId = claudeId };
            Save();
        }
    }

    private Dictionary<string, LedgerEntry> Entries() => _entries ??= Load();

    private Dictionary<string, LedgerEntry> Load()
    {
        var entries = new Dictionary<string, LedgerEntry>(StringComparer.Ordinal);
        if (AtomicFile.ReadShared(path) is not { Length: > 0 } json) return entries;
        try
        {
            var root = JsonNode.Parse(json);
            _shutdown = ReadShutdown(root?["shutdown"]);
            if (root?["sessions"] is not JsonArray list) return entries;
            foreach (var node in list)
            {
                string? S(string key) => TranscriptJson.AsString(node?[key]);
                if (S("id") is not { Length: > 0 } id || S("task") is not { Length: > 0 } task || S("cwd") is not { Length: > 0 } cwd) continue;
                var started = DateTimeOffset.TryParse(S("started"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : DateTimeOffset.MinValue;
                entries[id] = new LedgerEntry(id, task, cwd, S("configDir"), S("role"), S("mode"), S("claudeId") ?? id, started, S("model"), S("effort"));
            }
        }
        catch (JsonException)
        {
            // A damaged ledger costs only the extras (mode, post-/clear id); sessions still come back from their tasks.
        }
        return entries;
    }

    private void Save()
    {
        var list = new JsonArray();
        foreach (var e in Entries().Values.OrderBy(e => e.StartedAt))
        {
            var o = new JsonObject
            {
                ["id"] = e.Id,
                ["task"] = e.TaskId,
                ["cwd"] = e.Cwd,
                ["claudeId"] = e.ClaudeId,
                ["started"] = e.StartedAt.ToString("O", CultureInfo.InvariantCulture),
            };
            if (e.ConfigDir is not null) o["configDir"] = e.ConfigDir;
            if (e.Role is not null) o["role"] = e.Role;
            if (e.PermissionMode is not null) o["mode"] = e.PermissionMode;
            if (e.Model is not null) o["model"] = e.Model;
            if (e.Effort is not null) o["effort"] = e.Effort;
            list.Add((JsonNode)o);
        }
        var root = new JsonObject { ["sessions"] = list };
        if (_shutdown is { } stamp) root["shutdown"] = WriteShutdown(stamp);
        try
        {
            AtomicFile.Write(path, root.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the next change writes it again.
        }
    }

    private static string Iso(DateTimeOffset t) => t.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTime(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;

    private static JsonObject WriteShutdown(ShutdownStamp stamp)
    {
        var tasks = new JsonArray();
        foreach (var t in stamp.Tasks)
        {
            var o = new JsonObject { ["task"] = t.TaskId, ["state"] = t.PreviousState, ["autoNote"] = t.AutoNote };
            if (t.PreviousParkAt is { } at) o["parkAt"] = Iso(at);
            if (t.PreviousParkNote is not null) o["parkNote"] = t.PreviousParkNote;
            tasks.Add((JsonNode)o);
        }
        return new JsonObject { ["at"] = Iso(stamp.At), ["tasks"] = tasks };
    }

    private static ShutdownStamp? ReadShutdown(JsonNode? node)
    {
        if (ParseTime(TranscriptJson.AsString(node?["at"])) is not { } at) return null;
        var tasks = new List<ShutdownParkedTask>();
        if (node?["tasks"] is JsonArray list)
            foreach (var t in list)
            {
                string? S(string key) => TranscriptJson.AsString(t?[key]);
                if (S("task") is { Length: > 0 } id && S("state") is { Length: > 0 } state)
                    tasks.Add(new ShutdownParkedTask(id, state, ParseTime(S("parkAt")), S("parkNote"), S("autoNote") ?? ""));
            }
        return new ShutdownStamp(at, tasks);
    }
}
