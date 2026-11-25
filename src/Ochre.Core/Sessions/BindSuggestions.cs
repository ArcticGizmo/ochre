using Ochre.Core.Claude;
using Ochre.Core.Tasks;

namespace Ochre.Core.Sessions;

/// <param name="Reason">Why, in a few words: "folder is in ochre", "branch names PROJ-142".</param>
public sealed record BindSuggestion(string TaskId, string Reason);

/// <summary>
/// Which tasks a session started outside Ochre probably belongs to: its folder is inside one of the task's repos,
/// or its branch names a Jira issue the task links to. These are suggestions only; the user links it (task-model.md,
/// "Explicit over magic").
/// </summary>
public static class BindSuggestions
{
    public static IReadOnlyList<BindSuggestion> For(string cwd, string? branch, IEnumerable<TaskSummary> tasks)
    {
        var keys = JiraLink.Keys(branch);
        var found = new List<(BindSuggestion Suggestion, int Strength, DateTimeOffset? Updated)>();
        foreach (var t in tasks)
        {
            if (t.State == TaskState.Done) continue;
            var reasons = new List<string>();
            int strength = 0;
            if (keys.FirstOrDefault(k => t.Links.Any(l => NamesIssue(l, k))) is { } key)
            {
                reasons.Add($"branch names {key}");
                strength += 2;
            }
            if (t.Repos.FirstOrDefault(r => IsInside(cwd, r)) is { } repo)
            {
                reasons.Add($"folder is in {Leaf(repo)}");
                strength += 1;
            }
            if (strength > 0) found.Add((new BindSuggestion(t.Id, string.Join(", ", reasons)), strength, t.Updated));
        }
        return found.OrderByDescending(f => f.Strength).ThenByDescending(f => f.Updated).Select(f => f.Suggestion).ToList();
    }

    // A link names the issue by its key, or by a URL ending in it (…/browse/PROJ-142).
    private static bool NamesIssue(TaskLink link, string key) =>
        link.Ref.Equals(key, StringComparison.OrdinalIgnoreCase)
        || link.Ref.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or somewhere below it. Windows
    /// paths compare without case.</summary>
    public static bool IsInside(string path, string folder)
    {
        if (path.Length == 0 || folder.Length == 0) return false;
        try
        {
            var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return p.Equals(f, cmp) || (p.StartsWith(f, cmp) && (p[f.Length] == Path.DirectorySeparatorChar || p[f.Length] == Path.AltDirectorySeparatorChar));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string Leaf(string folder) => Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } leaf ? leaf : folder;
}
