using Ochre.Core.Sessions;
using Ochre.Core.Tasks;
using Xunit;

namespace Ochre.Tests;

/// <summary>Which task an outside session is offered for: a repo containing its folder, or a Jira key its branch names.</summary>
public sealed class BindSuggestionsTests
{
    private static TaskSummary Task(string id, TaskState state = TaskState.Active, string[]? repos = null, TaskLink[]? links = null, int minutesAgo = 0, string[]? worktrees = null) =>
        new(id, id, state, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), null, 0, 0, null, null, null, links ?? [], [], repos ?? [],
            (worktrees ?? []).Select(w => new TaskWorktree(w, repos?.FirstOrDefault() ?? "", "git-worktree", null, null)).ToList());

    [Fact]
    public void A_folder_in_a_tasks_worktree_outranks_a_repo_another_task_shares()
    {
        var s = BindSuggestions.For(@"C:\src\ochre.worktrees\t-0002-fix\ui", null,
            [Task("T-0001", repos: [@"C:\src\ochre"], minutesAgo: 0), Task("T-0002", repos: [@"C:\src\ochre"], minutesAgo: 60, worktrees: [@"C:\src\ochre.worktrees\t-0002-fix"])]);
        var only = Assert.Single(s);
        Assert.Equal("T-0002", only.TaskId);
        Assert.Equal("folder is its worktree t-0002-fix", only.Reason);
    }

    [Fact]
    public void A_folder_inside_a_tasks_repo_suggests_that_task()
    {
        var s = BindSuggestions.For(@"C:\src\ochre\ui", null, [Task("T-0001", repos: [@"C:\src\ochre"]), Task("T-0002", repos: [@"C:\src\other"])]);
        var only = Assert.Single(s);
        Assert.Equal("T-0001", only.TaskId);
        Assert.Equal("folder is in ochre", only.Reason);
    }

    [Fact]
    public void A_sibling_folder_with_the_same_prefix_is_not_inside()
    {
        Assert.False(BindSuggestions.IsInside(@"C:\src\ochre-old", @"C:\src\ochre"));
        Assert.True(BindSuggestions.IsInside(@"C:\src\ochre", @"C:\src\ochre\"));
    }

    [Fact]
    public void A_branch_naming_a_linked_issue_ranks_above_a_repo_match()
    {
        var jira = Task("T-0002", links: [new TaskLink("l1", "jira", "https://acme.atlassian.net/browse/PROJ-142", null, null)], minutesAgo: 60);
        var repo = Task("T-0001", repos: [@"C:\src\ochre"]);
        var s = BindSuggestions.For(@"C:\src\ochre", "feature/PROJ-142-shutdown", [repo, jira]);
        Assert.Equal(["T-0002", "T-0001"], s.Select(x => x.TaskId));
        Assert.Equal("branch names PROJ-142", s[0].Reason);
    }

    [Fact]
    public void Done_tasks_and_unrelated_sessions_get_nothing()
    {
        Assert.Empty(BindSuggestions.For(@"C:\src\ochre", "main", [Task("T-0001", TaskState.Done, repos: [@"C:\src\ochre"]), Task("T-0002")]));
    }
}
