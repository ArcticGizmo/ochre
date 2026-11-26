using Ochre.Core.Git;
using Ochre.Core.Modules;
using Ochre.Core.Tasks;

namespace Ochre.Modules.Git;

/// <summary>
/// <c>git worktree add</c> for a task: a checkout of one of its repos on its own branch, in a folder laid out the way
/// the user's other worktrees are (<see cref="WorktreeLayoutInference"/>). Adding and removing are things the user
/// asked for, so they run as <see cref="GitRunner.Trust.UserAction"/>: the repo's hooks run as they would in a
/// terminal.
/// </summary>
internal sealed class GitWorktreeProvider : IWorktreeProvider
{
    private const int ReadTimeoutMs = 10_000;

    // A checkout of a large repo, with its hooks and LFS smudging, can take a while.
    private const int WriteTimeoutMs = 180_000;

    public string Id => "git-worktree";
    public string Name => "Git worktree";
    public string RemoveLabel => "Remove worktree";
    public string RemoveDescription => "The folder and everything in it is deleted from disk, and the worktree comes off the task.";

    public Task<WorktreePlan> PlanAsync(string repo, string branch, CancellationToken ct) => Task.Run(() => Plan(repo, branch), ct);

    public Task<WorktreePlan> CreateAsync(string repo, string branch, CancellationToken ct) => Task.Run(() =>
    {
        var plan = Plan(repo, branch);
        var root = RepoOf(repo).Set.Root;
        var path = plan.Path!;
        string[] args = plan.NewBranch
            ? ["worktree", "add", "-b", plan.Branch, path]
            : ["worktree", "add", path, plan.Branch];
        var r = GitRunner.Run(root, WriteTimeoutMs, GitRunner.Trust.UserAction, null, args);
        if (r.Exit != 0) throw new WorktreeException(GitBranches.Error(r));
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
        int? unpushed = branch is null ? 0 : GitBranches.UnpushedCommits(root, branch);
        if (unpushed is null && GitBranches.Exists(root, branch!) == false) (branch, unpushed) = (null, 0);
        return new WorktreeCheck(exists, branch, changed, unpushed);
    }, ct);

    public Task RemoveAsync(TaskWorktree worktree, bool discardChanges, bool deleteBranch, CancellationToken ct) => Task.Run(() =>
    {
        var branch = CurrentBranch(worktree);
        var root = GitWorktreeScanner.Read(worktree.Repo)?.Root ?? worktree.Repo;
        if (Directory.Exists(worktree.Path) && IsRegistered(worktree))
        {
            var r = discardChanges
                ? GitRunner.Run(root, WriteTimeoutMs, GitRunner.Trust.UserAction, null, "worktree", "remove", "--force", worktree.Path)
                : GitRunner.Run(root, WriteTimeoutMs, GitRunner.Trust.UserAction, null, "worktree", "remove", worktree.Path);
            if (r.Exit != 0)
            {
                // On Windows git deletes the files and its record first, then fails on a folder a shell or editor has
                // open. What's left is an empty folder, which a second try clears.
                if (!IsRegistered(worktree))
                    throw new WorktreeException("git removed the worktree's files but couldn't delete the folder itself; a terminal, editor or other program probably has it open. Close it, then remove again to finish.");
                throw new WorktreeException(GitBranches.Error(r));
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

        if (deleteBranch && branch is not null && GitBranches.Exists(root, branch) == true && GitBranches.Delete(root, branch) is { } why)
            throw new WorktreeException($"the worktree is removed, but {branch} wasn't deleted: {why}");
    }, ct);

    /// <summary>Where <c>git worktree add</c> would put <paramref name="branch"/>, or why it can't. Blocking IO.</summary>
    internal static WorktreePlan Plan(string repo, string branch)
    {
        branch = branch.Trim();
        if (!GitBranches.IsValidName(branch)) throw new WorktreeException(GitBranches.NameRule);
        var (set, common) = RepoOf(repo);
        if (set.PathOfBranch(branch) is { } at) throw new WorktreeException($"{branch} is already checked out in {at}");

        var layout = WorktreeLayoutInference.Infer(set.Root, set.Bare, set.Linked,
            ReadIfExists(Path.Combine(set.Root, ".gitignore")), ReadIfExists(Path.Combine(common, "info", "exclude")));
        var path = layout.Render(set.Root, branch.Replace('/', '-'));
        if (File.Exists(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
            throw new WorktreeException($"{path} already exists");

        var exists = GitBranches.Exists(set.Root, branch) ?? throw new WorktreeException($"git couldn't say whether {branch} exists in {set.Root}");
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
}
