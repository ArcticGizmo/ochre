using System.Globalization;
using System.Text.RegularExpressions;

namespace Ochre.Core.Git;

/// <summary>
/// Questions about a repo's branches that making and removing a checkout depend on, whoever makes the checkout (a git
/// worktree, a sprig slot). Blocking; call off the UI thread.
/// </summary>
internal static partial class GitBranches
{
    private const int TimeoutMs = 10_000;

    /// <summary>Why a name isn't accepted, for the user.</summary>
    public const string NameRule = "a branch name is letters, digits and . _ - /, with no spaces, no \"..\" and no part starting with a dot";

    // Narrower than git's own rules (git check-ref-format): no spaces, globs or control characters, no component
    // starting with a dot or ending in .lock, no "..", "//" or leading dash, so it is also a safe folder name and
    // safe on a command line.
    [GeneratedRegex(@"^(?!-)(?!.*\.\.)(?!.*//)(?!.*(?:^|/)\.)(?!.*\.lock(?:/|$))(?!.*[/.]$)[A-Za-z0-9._/-]{1,100}$")]
    private static partial Regex Name();

    public static bool IsValidName(string branch) => Name().IsMatch(branch);

    /// <summary>Whether <paramref name="branch"/> exists; null when git couldn't say.</summary>
    public static bool? Exists(string root, string branch) =>
        GitRunner.Run(root, TimeoutMs, "rev-parse", "--verify", "--quiet", "refs/heads/" + branch).Exit switch
        {
            0 => true,
            1 => false,
            _ => null,
        };

    /// <summary>
    /// Commits on the branch that no other local branch or remote-tracking ref has: what deleting the branch would
    /// lose. Null when git couldn't say (for one, because the branch is gone).
    /// </summary>
    public static int? UnpushedCommits(string root, string branch)
    {
        var r = GitRunner.Run(root, TimeoutMs, "rev-list", "--count", "refs/heads/" + branch, "--not", "--exclude=" + branch, "--branches", "--remotes");
        return r.Exit == 0 && int.TryParse(r.Stdout.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>Deletes the branch whatever it holds; the caller has asked the user. Null on success, else git's reason.</summary>
    public static string? Delete(string root, string branch)
    {
        var r = GitRunner.Run(root, TimeoutMs, GitRunner.Trust.UserAction, null, "branch", "-D", branch);
        return r.Exit == 0 ? null : Error(r);
    }

    /// <summary>git's own message without its "fatal:"/"error:" prefixes; the user acts on it.</summary>
    public static string Error(GitRunner.Result r)
    {
        if (r.Exit == -1) return "git didn't finish (it timed out, or couldn't be started)";
        var lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("hint:", StringComparison.Ordinal))
            .Select(l => l.StartsWith("fatal: ", StringComparison.Ordinal) ? l[7..] : l.StartsWith("error: ", StringComparison.Ordinal) ? l[7..] : l)
            .ToList();
        return lines.Count > 0 ? string.Join(" ", lines) : $"git exited with code {r.Exit}";
    }
}
