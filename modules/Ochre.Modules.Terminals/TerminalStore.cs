using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ochre.Core.Claude;
using Ochre.Core.Platform;

namespace Ochre.Modules.Terminals;

/// <summary>A terminal tab as saved for the next start: where it ran and how, never what it printed.</summary>
public sealed record SavedTerminal(string Id, string ProfileId, string Folder, string? TaskId, string? StartupCommand, string? Title, bool KeepScrollback);

/// <summary>What <see cref="TerminalStore.Load"/> found.</summary>
/// <param name="ReadFailed">The list exists but couldn't be read; it must not be overwritten this session.</param>
public sealed record SavedTerminals(IReadOnlyList<SavedTerminal> Terminals, DateTimeOffset? SavedAt, bool ReadFailed);

/// <summary>
/// Terminal tabs on disk (ADR 0011, slice 3): <c>terminals.json</c> lists the tabs, and each tab that keeps its
/// scrollback has <c>&lt;id&gt;.scrollback</c>, encrypted for the current user (security threat 6). Every write is
/// atomic, so a crash leaves the old file or the new one.
/// </summary>
public sealed partial class TerminalStore(string folder, IDataProtector protector)
{
    private string ListPath => Path.Combine(folder, "terminals.json");

    private string ScrollbackPath(string id) => Path.Combine(folder, id + ".scrollback");

    // Ids come back from a file anyone with the user's rights can edit, and they name files.
    [GeneratedRegex("^t-[0-9a-f]{8}$")]
    public static partial Regex IdPattern();

    public SavedTerminals Load()
    {
        var read = AtomicFile.TryRead(ListPath, out var json);
        if (read == AtomicFile.ReadResult.Missing) return new([], null, false);
        if (read == AtomicFile.ReadResult.Failed) return new([], null, true);
        var list = new List<SavedTerminal>();
        DateTimeOffset? savedAt = null;
        try
        {
            var root = JsonNode.Parse(json);
            if (DateTimeOffset.TryParse(TranscriptJson.AsString(root?["savedAt"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) savedAt = at;
            if (root?["terminals"] is JsonArray items)
            {
                foreach (var node in items)
                {
                    string? S(string key) => TranscriptJson.AsString(node?[key]);
                    if (S("id") is not { } id || !IdPattern().IsMatch(id) || list.Any(t => t.Id == id)) continue;
                    if (S("profile") is not { Length: > 0 } profile || S("folder") is not { Length: > 0 } dir) continue;
                    var keep = node?["keepScrollback"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : true;
                    list.Add(new SavedTerminal(id, profile, dir, S("task"), S("command"), S("title"), keep));
                }
            }
        }
        catch (JsonException)
        {
            // A damaged list is treated as unreadable, so it isn't replaced by an empty one before someone looks.
            return new([], null, true);
        }
        return new(list, savedAt, false);
    }

    public void SaveList(IEnumerable<SavedTerminal> terminals, DateTimeOffset savedAt)
    {
        var items = new JsonArray();
        foreach (var t in terminals)
        {
            var o = new JsonObject { ["id"] = t.Id, ["profile"] = t.ProfileId, ["folder"] = t.Folder, ["keepScrollback"] = t.KeepScrollback };
            if (t.TaskId is not null) o["task"] = t.TaskId;
            if (t.StartupCommand is not null) o["command"] = t.StartupCommand;
            if (t.Title is not null) o["title"] = t.Title;
            items.Add((JsonNode)o);
        }
        var root = new JsonObject { ["savedAt"] = savedAt.ToString("O", CultureInfo.InvariantCulture), ["terminals"] = items };
        AtomicFile.Write(ListPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void SaveScrollback(string id, string text)
    {
        if (!IdPattern().IsMatch(id)) throw new ArgumentException("not a terminal id", nameof(id));
        var sealedBytes = protector.Protect(Encoding.UTF8.GetBytes(text));
        AtomicFile.Write(ScrollbackPath(id), s => s.Write(sealedBytes));
    }

    /// <summary>The saved scrollback, or null when there is none or it can't be decrypted.</summary>
    public string? LoadScrollback(string id)
    {
        if (!IdPattern().IsMatch(id)) return null;
        try
        {
            var bytes = File.ReadAllBytes(ScrollbackPath(id));
            return protector.Unprotect(bytes) is { } plain ? Encoding.UTF8.GetString(plain) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void DeleteScrollback(string id)
    {
        if (!IdPattern().IsMatch(id)) return;
        try
        {
            File.Delete(ScrollbackPath(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retried on the next change.
        }
    }

    public bool HasScrollback(string id) => IdPattern().IsMatch(id) && File.Exists(ScrollbackPath(id));
}
