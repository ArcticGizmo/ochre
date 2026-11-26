using System.Text.RegularExpressions;
using Ochre.Core.Modules;
using Ochre.Core.Tasks;

namespace Ochre.Modules.Git;

/// <summary>
/// <c>git worktree add</c> for a task: a checkout of one of its repos on its own branch, in a folder laid out the way
/// the user's other worktrees are (<see cref="WorktreeLayoutInference"/>). Adding and removing are things the user
/// asked for, so they run as <see cref="GitRunner.Trust.UserAction"/>: the repo's hooks run as they would in a
/// terminal.
/// </summary>
internal sealed partial class GitWorktreeProvider : IWorktreeProvider
{
    private const int ReadTimeoutMs = 10_000;

    // A checkout of a large repo, with its hooks and LFS smudging, can take a while.
    private const int WriteTimeoutMs = 180_000;

    public string Id => "git-worktree";
    public string Name => "Git worktree";

    // Narrower than git's own rules (git check-ref-format): no spaces, globs or control characters, no component
    // starting with a dot or ending in .lock, no "..", "//" or leading dash, so it is also a safe folder name.
    [GeneratedRegex(@"^(?!-)(?!.*\.\.)(?!.*//)(?!.*(?:^|/)\.)(?!.*\.lock(?:/|$))(?!.*[/.]$)[A-Za-z0-9._/-]{1,100}$")]
    private static partial Regex BranchName();

    public static bool IsBranchName(string branch) => BranchName().IsMatch(branch);

    public Task<WorktreePlan> PlanAsync(string repo, string branch, CancellationToken ct) => Task.Run(() => Plan(repo, branch), ct);

    public Task<WorktreePlan> CreateAsync(string repo, string branch, CancellationToken ct) => Task.Run(() =>
    {
        var plan = Plan(repo, branch);
        var root = RepoOf(repo).Set.Root;
        string[] args = plan.NewBranch
            ? ["worktree", "add", "-b", plan.Branch, plan.Path]
            : ["worktree", "add", plan.Path, plan.Branch];
        var r = GitRunner.Run(root, WriteTimeoutMs, GitRunner.Trust.UserAction, null, args);
        if (r.Exit != 0) throw new WorktreeException(Error(r));
        return plan;
    }, ct);

    public Task<WorktreeCheck> CheckAsync(TaskWorktree worktree, CancellationToken ct) => Task.Run(() =>
    {
        var registered = IsRegistered(worktree);
        // A folder git no longer knows as a worktree holds no work git can count; an empty one holds nothing at all.
        var exists = Directory.Exists(worktree.Path) && (registered || HasEntries(worktree.Path));
        var changed = registered && exists ? GitStatusReader.Read(worktree.Path)?.Changes.Count ?? 0 : 0;
        var branch = CurrentBranch(worktree);
        var root = GitWorktreeScanner.Read(worktree.Repo)?.Root ?? worktree.Repo;
        int? unpushed = branch is null ? 0 : UnpushedCommits(root, branch);
        if (unpushed is null && !BranchExists(root, branch!)) (branch, unpushed) = (null, 0);
        return new WorktreeCheck(exists, branch, changed, unpushed);
    }, ct);

    public Task RemoveAsync(TaskWorktree worktree, bool discardChanges, bool deleteBranch, CancellationToken ct) => Task.Run(() =>
    {
        var branch = CurrentBranch(worktree);
        var root = GitWorktreeScanner.Read(worktree.Repo)?.Root ?? worktree.Repo;
        GitRunner.Result r;
        if (Directory.Exists(worktree.Path) && IsRegistered(worktree))
        {
            r = discardChanges
                ? GitRunner.Run(root, WriteTimeoutMs, GitRunner.Trust.UserAction, null, "worktree", "remove", "--force", worktree.Path)
                : GitRunner.Run(root, WriteTimeoutMs, GitRunner.Trust.UserAction, null, "worktree", "remove", worktree.Path);
            if (r.Exit != 0)
            {
                // On Windows git deletes the files and its record first, then fails on a folder a shell or editor has
                // open. What's left is an empty folder, which a second try clears.
                if (!IsRegistered(worktree))
                    throw new WorktreeException("git removed the worktree's files but couldn't delete the folder itself; a terminal, editor or other program probably has it open. Close it, then remove again to finish.");
                throw new WorktreeException(Error(r));
            }
        }
        else
        {
            if (Directory.Exists(worktree.Path))
            {
                if (HasEntries(worktree.Path))
                    throw new WorktreeException($"{worktree.Path} is no longer a git worktree but still has files in it. Move or delete them yourself, then remove again.");
                try
                {
                    Directory.Delete(worktree.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new WorktreeException($"the empty folder {worktree.Path} couldn't be deleted; a terminal, editor or other program probably has it open. Close it, then remove again.");
                }
            }
            // Deleted by hand (or half removed): git still lists it until pruned.
            GitRunner.Run(root, ReadTimeoutMs, GitRunner.Trust.UserAction, null, "worktree", "prune");
        }

        if (deleteBranch && branch is not null && BranchExists(root, branch))
        {
            r = GitRunner.Run(root, ReadTimeoutMs, GitRunner.Trust.UserAction, null, "branch", "-D", branch);
            if (r.Exit != 0) throw new WorktreeException($"the worktree is removed, but {branch} wasn't deleted: {Error(r)}");
        }
    }, ct);

    /// <summary>Where <c>git worktree add</c> would put <paramref name="branch"/>, or why it can't. Blocking IO.</summary>
    internal static WorktreePlan Plan(string repo, string branch)
    {
        branch = branch.Trim();
        if (!IsBranchName(branch))
            throw new WorktreeException("a branch name is letters, digits and . _ - /, with no spaces, no \"..\" and no part starting with a dot");
        var (set, common) = RepoOf(repo);
        if (set.PathOfBranch(branch) is { } at) throw new WorktreeException($"{branch} is already checked out in {at}");

        var layout = WorktreeLayoutInference.Infer(set.Root, set.Bare, set.Linked,
            ReadIfExists(Path.Combine(set.Root, ".gitignore")), ReadIfExists(Path.Combine(common, "info", "exclude")));
        var path = layout.Render(set.Root, branch.Replace('/', '-'));
        if (File.Exists(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
            throw new WorktreeException($"{path} already exists");

        var exists = BranchExists(set.Root, branch);
        var from = exists ? null : GitWorktreeScanner.BranchOfHead(Path.Combine(common, "HEAD")) ?? "the current commit";
        return new WorktreePlan(path, branch, !exists, from);
    }

    private static (GitWorktreeSet Set, string Common) RepoOf(string repo)
    {
        if (GitCheckoutScanner.FindRoot(repo) is not { } found
            || GitWorktreeScanner.Read(found) is not { } set
            || GitWorktreeScanner.CommonDir(set.Root) is not { } common)
            throw new WorktreeException($"{repo} isn't a git repository");
        return (set, common);
    }

    private static GitWorktree? Linked(TaskWorktree worktree) =>
        GitWorktreeScanner.Read(worktree.Repo)?.Linked
            .FirstOrDefault(w => string.Equals(Path.TrimEndingDirectorySeparator(w.Path), Path.TrimEndingDirectorySeparator(worktree.Path), StringComparison.OrdinalIgnoreCase));

    private static bool IsRegistered(TaskWorktree worktree) => Linked(worktree) is not null;

    // The branch the checkout has now (an agent may have switched it), else the one it was made with.
    private static string? CurrentBranch(TaskWorktree worktree) => Linked(worktree) is { } linked ? linked.Branch : worktree.Branch;

    private static bool HasEntries(string folder)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(folder).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool BranchExists(string root, string branch)
    {
        var r = GitRunner.Run(root, ReadTimeoutMs, "rev-parse", "--verify", "--quiet", "refs/heads/" + branch);
        return r.Exit switch
        {
            0 => true,
            1 => false,
            _ => throw new WorktreeException(Error(r)),
        };
    }

    // Commits on the branch that no other local branch or remote-tracking ref has; null when git couldn't say
    // (for one, because the branch is gone).
    private static int? UnpushedCommits(string root, string branch)
    {
        var r = GitRunner.Run(root, ReadTimeoutMs, "rev-list", "--count", "refs/heads/" + branch, "--not", "--exclude=" + branch, "--branches", "--remotes");
        return r.Exit == 0 && int.TryParse(r.Stdout.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static string? ReadIfExists(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // git's own message without its "fatal:"/"error:" prefixes; the user acts on it.
    internal static string Error(GitRunner.Result r)
    {
        if (r.Exit == -1) return "git didn't finish (it timed out, or couldn't be started)";
        var lines = r.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("hint:", StringComparison.Ordinal))
            .Select(l => l.StartsWith("fatal: ", StringComparison.Ordinal) ? l[7..] : l.StartsWith("error: ", StringComparison.Ordinal) ? l[7..] : l)
            .ToList();
        return lines.Count > 0 ? string.Join(" ", lines) : $"git exited with code {r.Exit}";
    }
}
