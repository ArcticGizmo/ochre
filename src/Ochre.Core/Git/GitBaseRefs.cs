namespace Ochre.Core.Git;

/// <summary>
/// The branch a checkout was made from: the plausible base branches to measure it against, and where it left one.
/// Best-effort; empty or null on any failure.
/// </summary>
internal static class GitBaseRefs
{
    private const int TimeoutMs = 4000;

    // Branch names recognised as a trunk to scope against, most likely first. A ref only becomes a base candidate if its
    // leaf name is one of these.
    private static readonly string[] TrunkNames = ["main", "master", "trunk", "develop"];

    /// <summary>The merge-base of HEAD and <paramref name="baseRef"/> (where the current branch left it) as a full
    /// hash, or null on any failure.</summary>
    public static string? MergeBase(string cwd, string baseRef)
    {
        if (string.IsNullOrEmpty(baseRef) || GitCheckoutScanner.FindRoot(cwd) is null)
            return null;
        var r = GitRunner.Run(cwd, TimeoutMs, "--no-optional-locks", "merge-base", "HEAD", baseRef);
        if (r.Exit != 0)
            return null;
        var hash = r.Stdout.Trim();
        return hash.Length == 0 ? null : hash;
    }

    /// <summary>
    /// The candidate base branches for the checkout, best first: a fork setup's <c>upstream/main</c> before
    /// <c>origin/main</c> before a local <c>main</c>. <paramref name="currentBranch"/> is never its own base.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string cwd, string? currentBranch)
    {
        if (GitCheckoutScanner.FindRoot(cwd) is null)
            return [];
        var r = GitRunner.Run(cwd, TimeoutMs, "--no-optional-locks", "for-each-ref", "--format=%(refname:short)", "refs/heads", "refs/remotes");
        if (r.Exit != 0)
            return [];
        return Pick(currentBranch, r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Ranks branch refs into plausible base branches, best first. By tier: a remote named <c>upstream</c> (the fork
    /// convention), then <c>origin</c>, then local branches, then any other remote; within a tier by trunk name
    /// (<c>main</c> &gt; <c>master</c> &gt; <c>trunk</c> &gt; <c>develop</c>). Only trunk-named refs qualify;
    /// <c>*/HEAD</c> and <paramref name="currentBranch"/> (locally) are dropped; duplicates removed in order. Pure.
    /// </summary>
    internal static IReadOnlyList<string> Pick(string? currentBranch, IReadOnlyList<string> refs)
    {
        var ranked = new List<(int Tier, int Name, string Ref)>();
        foreach (var r in refs)
        {
            if (string.IsNullOrEmpty(r))
                continue;
            var slash = r.IndexOf('/', StringComparison.Ordinal);
            var leaf = slash >= 0 ? r[(slash + 1)..] : r;
            if (leaf == "HEAD")
                continue; // origin/HEAD and friends are symbolic pointers, not a base
            var name = Array.IndexOf(TrunkNames, leaf);
            if (name < 0)
                continue;

            int tier;
            if (slash < 0)
            {
                if (r == currentBranch)
                    continue;
                tier = 2;
            }
            else
            {
                tier = r[..slash] switch { "upstream" => 0, "origin" => 1, _ => 3 };
            }
            ranked.Add((tier, name, r));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return ranked.OrderBy(x => x.Tier).ThenBy(x => x.Name).Select(x => x.Ref).Where(seen.Add).ToList();
    }
}
