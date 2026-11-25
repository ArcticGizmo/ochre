using Spike.Core.Bridge;

namespace Spike.Core;

/// <summary>Reads the hard-coded sample tasks. Just enough frontmatter parsing to show a list.</summary>
public static class SampleTasks
{
    public static TaskSummary[] Load()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "sample-tasks");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.md").Order(StringComparer.Ordinal).Select(Parse).ToArray();
    }

    private static TaskSummary Parse(string path)
    {
        var lines = File.ReadAllLines(path);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var bodyStart = 0;
        if (lines.Length > 0 && lines[0] == "---")
        {
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i] == "---")
                {
                    bodyStart = i + 1;
                    break;
                }
                var colon = lines[i].IndexOf(':', StringComparison.Ordinal);
                if (colon > 0 && !char.IsWhiteSpace(lines[i][0]))
                    fields[lines[i][..colon]] = lines[i][(colon + 1)..].Trim();
            }
        }
        return new TaskSummary(
            fields.GetValueOrDefault("id", Path.GetFileNameWithoutExtension(path)),
            fields.GetValueOrDefault("title", "(untitled)"),
            fields.GetValueOrDefault("state", "backlog"),
            string.Join('\n', lines.Skip(bodyStart)).Trim());
    }
}
