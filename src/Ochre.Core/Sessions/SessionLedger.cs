using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ochre.Core.Claude;

namespace Ochre.Core.Sessions;

/// <param name="Id">The id the session was launched with; the key the task knows it by.</param>
/// <param name="ClaudeId">The Claude session to resume: the launch id until a <c>/clear</c> moves it to a new one.</param>
public sealed record LedgerEntry(
    string Id,
    string TaskId,
    string Cwd,
    string? ConfigDir,
    string? Role,
    string? PermissionMode,
    string ClaudeId,
    DateTimeOffset StartedAt);

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
            if (JsonNode.Parse(json)?["sessions"] is not JsonArray list) return entries;
            foreach (var node in list)
            {
                string? S(string key) => TranscriptJson.AsString(node?[key]);
                if (S("id") is not { Length: > 0 } id || S("task") is not { Length: > 0 } task || S("cwd") is not { Length: > 0 } cwd) continue;
                var started = DateTimeOffset.TryParse(S("started"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : DateTimeOffset.MinValue;
                entries[id] = new LedgerEntry(id, task, cwd, S("configDir"), S("role"), S("mode"), S("claudeId") ?? id, started);
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
            list.Add((JsonNode)o);
        }
        try
        {
            AtomicFile.Write(path, new JsonObject { ["sessions"] = list }.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the next change writes it again.
        }
    }
}
