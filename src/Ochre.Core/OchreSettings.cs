using System.Text.Json;
using System.Text.Json.Nodes;
using Ochre.Core.Claude;

namespace Ochre.Core;

/// <summary>
/// The user's settings, one small JSON file in the roaming folder. Keys Ochre doesn't know are kept on save, so a
/// newer version's settings survive an older one. A file that can't be parsed is set aside as
/// <c>settings.json.bad</c> rather than overwritten, and the defaults apply. Saves are atomic.
/// </summary>
public sealed class OchreSettings(string path)
{
    private readonly Lock _gate = new();
    private JsonObject? _root;

    // The setting the shutdown-park module replaced (ADR 0012); a true here turns the module on until the user chooses.
    private const string LegacyAutoPark = "autoParkOnShutdown";

    /// <summary>Whether the user switched a module on or off; null when they haven't, so its default applies.</summary>
    public bool? ModuleEnabled(string id)
    {
        lock (_gate)
        {
            try
            {
                if (Root()["modules"] is JsonObject modules && modules[id] is JsonValue v && v.TryGetValue<bool>(out var on)) return on;
                if (id == "shutdown-park" && Root()[LegacyAutoPark] is JsonValue legacy && legacy.TryGetValue<bool>(out var park) && park) return true;
            }
            catch (InvalidOperationException)
            {
                // The wrong type, from a hand edit: treat as not chosen.
            }
            return null;
        }
    }

    /// <summary>Records the user's choices; they apply at the next start.</summary>
    public void SetModules(IReadOnlyDictionary<string, bool> choices)
    {
        lock (_gate)
        {
            if (Root()["modules"] is not JsonObject modules) Root()["modules"] = modules = [];
            foreach (var (id, on) in choices) modules[id] = on;
            Root().Remove(LegacyAutoPark);
            AtomicFile.Write(path, Root().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private JsonObject Root()
    {
        if (_root is not null) return _root;
        var text = AtomicFile.ReadShared(path);
        if (string.IsNullOrWhiteSpace(text)) return _root = [];
        try
        {
            if (JsonNode.Parse(text) is JsonObject o) return _root = o;
        }
        catch (JsonException)
        {
        }
        try
        {
            File.Copy(path, path + ".bad", overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return _root = [];
    }
}
