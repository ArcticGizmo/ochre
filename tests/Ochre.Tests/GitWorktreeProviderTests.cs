using Ochre.Core.Claude;
using Ochre.Core.Modules;
using Ochre.Core.Tasks;
using Ochre.Modules.Git;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Removing a task's worktree is where work can be lost, so these run against a real repo: the check counts what
/// would go, a checkout with changes isn't removed unless the user agreed, and the branch stays unless asked.
/// </summary>
public sealed class GitWorktreeProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ochre-worktrees-").FullName;
    private readonly string? _git = ExecutableResolver.Find("git");
    private readonly GitWorktreeProvider _provider = new();

    public void Dispose() => GitRunnerTests.DeleteTree(_dir);

    private string Repo()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_dir, "web")).FullName;
        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "config", "user.email", "t@example.com");
        Git(repo, "config", "user.name", "t");
        Git(repo, "config", "commit.gpgsign", "false");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "hello\n");
        Git(repo, "add", "-A");
        Git(repo, "commit", "-q", "-m", "init");
        return repo;
    }

    private void Git(string cwd, params string[] args) => GitRunnerTests.Git(_git!, cwd, args);

    private static bool BranchExists(string repo, string branch) =>
        GitRunner.Run(repo, 10_000, "rev-parse", "--verify", "--quiet", "refs/heads/" + branch).Exit == 0;

    [Fact]
    public async Task A_new_worktree_lands_beside_the_repo_on_a_new_branch()
    {
        // Needs git on the host.
        if (_git is null) return;
        var repo = Repo();

        var plan = await _provider.PlanAsync(repo, "t-0001/fix", CancellationToken.None);
        Assert.Equal(Path.Combine(_dir, "web.worktrees", "t-0001-fix"), plan.Path);
        Assert.True(plan.NewBranch);
        Assert.Equal("main", plan.Base);

        var made = await _provider.CreateAsync(repo, "t-0001/fix", CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(made.Path, "a.txt")));
        Assert.Equal("t-0001/fix", GitStatusReader.Read(made.Path)!.Value.Branch);

        // Checked out now, so a second one on the same branch is refused.
        var refused = await Assert.ThrowsAsync<WorktreeException>(() => _provider.PlanAsync(repo, "t-0001/fix", CancellationToken.None));
        Assert.Contains("already checked out", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_reports_what_would_be_lost_and_keeps_the_branch_unless_asked()
    {
        if (_git is null) return;
        var repo = Repo();
        var made = await _provider.CreateAsync(repo, "t-0002", CancellationToken.None);
        var worktree = new TaskWorktree(made.Path, repo, _provider.Id, made.Branch, null);

        var clean = await _provider.CheckAsync(worktree, CancellationToken.None);
        Assert.Equal(new WorktreeCheck(true, "t-0002", 0, 0), clean);

        File.WriteAllText(Path.Combine(made.Path, "b.txt"), "work\n");
        Git(made.Path, "add", "-A");
        Git(made.Path, "commit", "-q", "-m", "work");
        File.WriteAllText(Path.Combine(made.Path, "c.txt"), "not committed\n");

        var busy = await _provider.CheckAsync(worktree, CancellationToken.None);
        Assert.Equal(new WorktreeCheck(true, "t-0002", 1, 1), busy);

        // Uncommitted work: refused without the user's say-so.
        await Assert.ThrowsAsync<WorktreeException>(() => _provider.RemoveAsync(worktree, discardChanges: false, deleteBranch: false, CancellationToken.None));
        Assert.True(Directory.Exists(made.Path));

        await _provider.RemoveAsync(worktree, discardChanges: true, deleteBranch: false, CancellationToken.None);
        Assert.False(Directory.Exists(made.Path));
        Assert.True(BranchExists(repo, "t-0002"));

        // Gone from disk: removing again only tidies up, and now deletes the branch as asked.
        var gone = await _provider.CheckAsync(worktree, CancellationToken.None);
        Assert.False(gone.Exists);
        Assert.Equal(1, gone.UnpushedCommits);
        await _provider.RemoveAsync(worktree, discardChanges: false, deleteBranch: true, CancellationToken.None);
        Assert.False(BranchExists(repo, "t-0002"));
    }

    // On Windows git deletes a worktree's files and its record, then fails on a folder a shell has open, leaving an
    // empty folder the task still lists. Removing again must finish the job, and never delete files git didn't.
    [Fact]
    public async Task A_half_removed_worktree_is_finished_off_but_stray_files_are_left_alone()
    {
        if (_git is null) return;
        var repo = Repo();
        var made = await _provider.CreateAsync(repo, "t-0003", CancellationToken.None);
        var worktree = new TaskWorktree(made.Path, repo, _provider.Id, made.Branch, null);
        Git(repo, "worktree", "remove", made.Path);
        Directory.CreateDirectory(made.Path);

        var check = await _provider.CheckAsync(worktree, CancellationToken.None);
        Assert.False(check.Exists);
        Assert.Equal(0, check.Changed);
        await _provider.RemoveAsync(worktree, discardChanges: false, deleteBranch: false, CancellationToken.None);
        Assert.False(Directory.Exists(made.Path));

        Directory.CreateDirectory(made.Path);
        File.WriteAllText(Path.Combine(made.Path, "keep.txt"), "mine\n");
        Assert.True((await _provider.CheckAsync(worktree, CancellationToken.None)).Exists);
        await Assert.ThrowsAsync<WorktreeException>(() => _provider.RemoveAsync(worktree, discardChanges: true, deleteBranch: false, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(made.Path, "keep.txt")));
    }

    [Theory]
    [InlineData("t-0042-fix-login", true)]
    [InlineData("feature/T-0042_x.y", true)]
    [InlineData("-x", false)]
    [InlineData("a..b", false)]
    [InlineData("a b", false)]
    [InlineData(".hidden", false)]
    [InlineData("a/.hidden", false)]
    [InlineData("a//b", false)]
    [InlineData("a/", false)]
    [InlineData("x.lock", false)]
    [InlineData("a@{1}", false)]
    [InlineData("a*", false)]
    public void Branch_names_are_checked_before_git_sees_them(string name, bool ok) =>
        Assert.Equal(ok, GitWorktreeProvider.IsBranchName(name));
}
