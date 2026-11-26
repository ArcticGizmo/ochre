namespace Ochre.Modules.Git;

/// <summary>
/// Reads what a checkout is from disk rather than running <c>git</c>: a task can name several folders and every
/// window refreshes them on focus, so a process per question would be slow and noisy for no gain. Pure apart from
/// <see cref="FindRoot"/>'s existence checks; never throws.
/// </summary>
internal static class GitCheckoutScanner
{
    /// <summary>The nearest folder at or above <paramref name="dir"/> holding a <c>.git</c> directory or file.</summary>
    public static string? FindRoot(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try
        {
            for (var d = new DirectoryInfo(Path.GetFullPath(dir)); d is not null; d = d.Parent)
            {
                var git = Path.Combine(d.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git)) return d.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        return null;
    }

    /// <summary>The value of <c>key</c> in a plain <c>[section]</c> (no subsection) of a git config file, last one
    /// winning as git does; null when absent. Pure.</summary>
    internal static string? ConfigValue(string config, string section, string key)
    {
        string? value = null;
        var inSection = false;
        foreach (var raw in config.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (line[0] == '[')
            {
                var close = line.IndexOf(']', StringComparison.Ordinal);
                inSection = close > 0 && line[1..close].Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0 || !line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = StripComment(line[(eq + 1)..].Trim());
            value = v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;
        }
        return value;
    }

    // An unquoted value ends at an inline comment.
    private static string StripComment(string value)
    {
        if (value.StartsWith('"')) return value;
        var cut = value.IndexOfAny(['#', ';']);
        return (cut >= 0 ? value[..cut] : value).Trim();
    }
}
