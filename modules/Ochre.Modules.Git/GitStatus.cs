namespace Ochre.Modules.Git;

/// <summary>
/// How a single path changed, in one of git's index/worktree slots. Mirrors the status codes git reports
/// in <c>--porcelain=v2</c> (X = staged, Y = unstaged). <see cref="None"/> is the unchanged slot (git's
/// <c>.</c>); <see cref="Unmerged"/> covers any conflict state (git's <c>u</c> records).
/// </summary>
internal enum GitChangeKind
{
    None,
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Unmerged,
}

/// <summary>
/// One changed path in a working tree, as read from <c>git status --porcelain=v2</c>. <see cref="Staged"/>
/// is the index-vs-HEAD change (git's X), <see cref="Unstaged"/> the worktree-vs-index change (git's Y);
/// either can be <see cref="GitChangeKind.None"/>. <see cref="OrigPath"/> is the pre-rename/-copy path when
/// <see cref="Staged"/> is <see cref="GitChangeKind.Renamed"/>/<see cref="GitChangeKind.Copied"/>, else null.
/// <see cref="Untracked"/> flags a file git isn't tracking yet (its "?" records), which carries no X/Y.
/// </summary>
internal readonly record struct GitFileChange(
    string Path,
    string? OrigPath,
    GitChangeKind Staged,
    GitChangeKind Unstaged,
    bool Untracked);

/// <summary>
/// A snapshot of a working tree's git status: the current branch (null when detached), its upstream ref
/// (null when none), how many commits it is <see cref="Ahead"/>/<see cref="Behind"/> that upstream, and the
/// changed paths. Best-effort: a field is simply left at its default when git didn't report it.
/// </summary>
internal readonly record struct GitRepoStatus(
    string? Branch,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<GitFileChange> Changes)
{
    /// <summary>True when the working tree has no reported changes.</summary>
    public bool IsClean => Changes.Count == 0;
}

/// <summary>
/// Reads a working tree's status. Every run carries <c>--no-optional-locks</c> so it can't take <c>index.lock</c>
/// and get in the way of an agent's own git, and goes through <see cref="GitRunner"/> as an automatic run.
/// </summary>
internal static class GitStatusReader
{
    private const int TimeoutMs = 4000;

    /// <summary>The working tree's status, or null when <paramref name="folder"/> isn't a readable git checkout.
    /// Untracked directories stay collapsed to one entry (git's default); expanding them can walk a huge tree.</summary>
    public static GitRepoStatus? Read(string folder)
    {
        if (GitCheckoutScanner.FindRoot(folder) is null) return null;
        var r = GitRunner.Run(folder, TimeoutMs, "--no-optional-locks", "status", "--porcelain=v2", "--branch");
        return r.Exit == 0 ? ParseStatusV2(r.Stdout) : null;
    }

    /// <summary>
    /// Parses <c>git status --porcelain=v2 --branch</c> output. Header lines (<c># branch.head</c>,
    /// <c># branch.upstream</c>, <c># branch.ab</c>) fill the branch/upstream/ahead/behind fields; entry
    /// lines fill <see cref="GitRepoStatus.Changes"/>: <c>1</c> (ordinary), <c>2</c> (rename/copy, whose
    /// tab-separated trailer carries the original path), <c>u</c> (unmerged), <c>?</c> (untracked). Ignored
    /// (<c>!</c>) and unknown lines are skipped; blank/malformed lines never throw. Tolerates CRLF.
    /// </summary>
    internal static GitRepoStatus ParseStatusV2(string output)
    {
        string? branch = null, upstream = null;
        int ahead = 0, behind = 0;
        var changes = new List<GitFileChange>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            switch (line[0])
            {
                case '#':
                    ParseBranchHeader(line, ref branch, ref upstream, ref ahead, ref behind);
                    break;

                // "1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>"
                case '1':
                {
                    var p = line.Split(' ', 9);
                    if (p.Length < 9) break;
                    var (st, un) = MapXy(p[1]);
                    changes.Add(new GitFileChange(p[8], null, st, un, false));
                    break;
                }

                // "2 <XY> ... <score> <path>\t<origPath>"
                case '2':
                {
                    var p = line.Split(' ', 10);
                    if (p.Length < 10) break;
                    var (st, un) = MapXy(p[1]);
                    var tab = p[9].IndexOf('\t', StringComparison.Ordinal);
                    var path = tab >= 0 ? p[9][..tab] : p[9];
                    var orig = tab >= 0 ? p[9][(tab + 1)..] : null;
                    changes.Add(new GitFileChange(path, orig, st, un, false));
                    break;
                }

                // "u <xy> ... <path>": a conflict; both slots read as Unmerged.
                case 'u':
                {
                    var p = line.Split(' ', 11);
                    if (p.Length < 11) break;
                    changes.Add(new GitFileChange(p[10], null, GitChangeKind.Unmerged, GitChangeKind.Unmerged, false));
                    break;
                }

                // "? <path>"
                case '?':
                {
                    var p = line.Split(' ', 2);
                    if (p.Length < 2) break;
                    changes.Add(new GitFileChange(p[1], null, GitChangeKind.None, GitChangeKind.None, true));
                    break;
                }

                // '!' (ignored) and anything unexpected: skip.
            }
        }

        return new GitRepoStatus(branch, upstream, ahead, behind, changes);
    }

    // Folds one "# branch.*" header into the accumulating branch/upstream/ahead/behind.
    private static void ParseBranchHeader(string line, ref string? branch, ref string? upstream, ref int ahead, ref int behind)
    {
        const string head = "# branch.head ", up = "# branch.upstream ", ab = "# branch.ab ";
        if (line.StartsWith(head, StringComparison.Ordinal))
        {
            var name = line[head.Length..];
            branch = name == "(detached)" ? null : name;
        }
        else if (line.StartsWith(up, StringComparison.Ordinal))
        {
            upstream = line[up.Length..];
        }
        else if (line.StartsWith(ab, StringComparison.Ordinal))
        {
            // "+<ahead> -<behind>"
            foreach (var tok in line[ab.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (tok[0] == '+' && int.TryParse(tok.AsSpan(1), out var a)) ahead = a;
                else if (tok[0] == '-' && int.TryParse(tok.AsSpan(1), out var b)) behind = b;
            }
        }
    }

    // Maps a porcelain-v2 two-char "XY" field to (staged, unstaged) kinds. Anything unexpected is None.
    private static (GitChangeKind Staged, GitChangeKind Unstaged) MapXy(string xy)
    {
        var x = xy.Length > 0 ? xy[0] : '.';
        var y = xy.Length > 1 ? xy[1] : '.';
        return (MapCode(x), MapCode(y));
    }

    private static GitChangeKind MapCode(char c) => c switch
    {
        'M' => GitChangeKind.Modified,
        'A' => GitChangeKind.Added,
        'D' => GitChangeKind.Deleted,
        'R' => GitChangeKind.Renamed,
        'C' => GitChangeKind.Copied,
        'T' => GitChangeKind.TypeChanged,
        'U' => GitChangeKind.Unmerged,
        _ => GitChangeKind.None,
    };
}
