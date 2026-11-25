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

    /// <summary>When Windows shuts down or signs out, park every task with running agents, with an automatic note.</summary>
    public bool AutoParkOnShutdown
    {
        get => GetBool("autoParkOnShutdown");
        set => SetBool("autoParkOnShutdown", value);
    }

    private bool GetBool(string key)
    {
        lock (_gate)
        {
            try
            {
                return Root()[key]?.GetValue<bool>() ?? false;
            }
            catch (InvalidOperationException)
            {
                return false;   // the wrong type, from a hand edit
            }
        }
    }

    private void SetBool(string key, bool value)
    {
        lock (_gate)
        {
            Root()[key] = value;
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
