using Ochre.Core.Tasks;
using Xunit;

namespace Ochre.Tests;

/// <summary>
/// Task files are the one thing Ochre exists to keep, and people edit them by hand, so every write must leave
/// everything it didn't mean to change exactly as it was.
/// </summary>
public class TaskDocumentTests
{
    private const string Full = """
        ---
        id: T-0042
        title: Usage readout for multi-org accounts
        state: active
        created: 2026-11-25T09:00:00Z
        updated: 2026-11-25T16:40:00Z
        repos:
          - C:\git\example-app
          - C:\git\example-lib
        links:
          - id: l1
            kind: jira
            ref: PROJ-1234
            title: Usage bar wrong for second org
            summary: Monthly spend reads the primary org's value for every account.
          - id: l2
            kind: pr
            ref: example-org/example-app#120
        sessions:
          - id: 4a7cc5b2-0000-0000-0000-000000000000
            cwd: C:\git\example-app
            binding: launched
            role: implement
        park:
          at: 2026-11-24T17:55:00Z
          note: |
            Parser fix done and tested. Next: wire the per-account bar.
        ---

        ## Plan
        - [x] Reproduce with the two-org fixture {#p1}
        - [x] Fix the spend parser unit {#p2}
        - [ ] Per-account bar {#p3}
          - [ ] Colour by pace {#p3a}
        - [ ] Manual test doc {#p4}

        ## Notes
        Free-form notes, decisions, gotchas.

        ## Log
        - 2026-11-24T15:10Z session 4a7cc5b2 checked p2
        - 2026-11-24T17:55Z parked by user

        """;

    private const string HandEdited = """
        ---
        id: T-0007
        title: Hand written
        state: backlog
        colour: teal
        ---

        Some intro text the user wrote.

        ## Plan
        - [ ] First thing
        - [ ] Second thing {#p1}
          * [X] Nested with a star bullet
        - [ ] Duplicate id {#p1}

        ## Extra section
        Kept verbatim.
        """;

    private static string Lf(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Reads_every_field_of_the_documented_format()
    {
        var doc = TaskDocument.Parse(Lf(Full));

        Assert.Equal("T-0042", doc.Id);
        Assert.Equal("Usage readout for multi-org accounts", doc.Title);
        Assert.Equal(TaskState.Active, doc.State);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 16, 40, 0, TimeSpan.Zero), doc.Updated);
        Assert.Equal([@"C:\git\example-app", @"C:\git\example-lib"], doc.Repos);
        Assert.Equal(2, doc.Links.Count);
        Assert.Equal("PROJ-1234", doc.Links[0].Ref);
        Assert.Null(doc.Links[1].Title);
        Assert.Equal("implement", Assert.Single(doc.Sessions).Role);
        Assert.Equal("Parser fix done and tested. Next: wire the per-account bar.", doc.Park?.Note);

        var plan = doc.Plan;
        Assert.Equal(["p1", "p2", "p3", "p4"], plan.Select(p => p.Id));
        Assert.Equal("p3a", Assert.Single(plan[2].Children).Id);
        Assert.True(plan[0].Done);
        Assert.Equal("Free-form notes, decisions, gotchas.", doc.Notes);
        Assert.Equal(2, doc.Log.Count);
    }

    [Theory]
    [InlineData(Full)]
    [InlineData(HandEdited)]
    public void Unchanged_document_serialises_byte_identical(string text)
    {
        foreach (var variant in new[] { Lf(text), Lf(text).Replace("\n", "\r\n", StringComparison.Ordinal) })
            Assert.Equal(variant, TaskDocument.Parse(variant).Serialize());
    }

    [Fact]
    public void Re_emitted_frontmatter_matches_the_documented_layout()
    {
        var text = Lf(Full);
        var doc = TaskDocument.Parse(text);

        // Touch a field so the frontmatter is re-emitted rather than kept verbatim.
        var updated = doc.Updated!.Value;
        doc.Updated = updated.AddMinutes(1);
        doc.Updated = updated;

        Assert.Equal(text, doc.Serialize());
    }

    [Fact]
    public void Checking_an_item_changes_only_that_line()
    {
        var before = Lf(Full);
        var doc = TaskDocument.Parse(before);

        Assert.True(doc.SetItemDone("p3a", true));
        var after = doc.Serialize();

        var diff = before.Split('\n').Zip(after.Split('\n')).Where(p => p.First != p.Second).ToList();
        var (old, now) = Assert.Single(diff);
        Assert.Equal("  - [ ] Colour by pace {#p3a}", old);
        Assert.Equal("  - [x] Colour by pace {#p3a}", now);
        Assert.False(doc.SetItemDone("p3a", true));
        Assert.False(doc.SetItemDone("nope", true));
    }

    [Fact]
    public void A_full_write_keeps_unknown_keys_and_untouched_body_lines()
    {
        var before = Lf(HandEdited);
        var doc = TaskDocument.Parse(before);
        var now = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

        doc.EnsureItemIds();
        doc.Updated = now;
        doc.AppendLog(now, "user", "edited");
        var reparsed = TaskDocument.Parse(doc.Serialize());

        Assert.Contains("colour: teal", doc.Serialize(), StringComparison.Ordinal);
        Assert.Equal(now, reparsed.Updated);
        Assert.Contains("Some intro text the user wrote.", doc.Serialize(), StringComparison.Ordinal);
        Assert.Contains("## Extra section\nKept verbatim.", doc.Serialize(), StringComparison.Ordinal);
        Assert.Equal("2026-11-25T10:00Z user edited", Assert.Single(reparsed.Log).Text);
    }

    [Fact]
    public void Missing_and_duplicate_item_ids_are_assigned_after_the_highest()
    {
        var doc = TaskDocument.Parse(Lf(HandEdited));

        Assert.True(doc.EnsureItemIds());
        var ids = doc.AllPlanItems().OrderBy(p => p.Line).Select(p => p.Id).ToList();

        Assert.Equal(["p2", "p1", "p3", "p4"], ids);
        Assert.True(doc.FindItem("p3")!.Done);
        Assert.False(doc.EnsureItemIds());
    }

    [Fact]
    public void Added_items_get_new_ids_and_nest_under_their_parent()
    {
        var doc = TaskDocument.Parse(Lf(Full));

        var child = doc.AddItem("Pick colours", parentId: "p3");
        var top = doc.AddItem("Ship it");

        Assert.Equal("p5", child);
        Assert.Equal("p6", top);
        Assert.Equal(["p3a", "p5"], doc.FindItem("p3")!.Children.Select(c => c.Id));
        Assert.Equal("p6", doc.Plan[^1].Id);
        Assert.Contains("  - [ ] Pick colours {#p5}\n- [ ] Manual test doc {#p4}\n- [ ] Ship it {#p6}\n\n## Notes", doc.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_task_round_trips_and_re_emits_stably()
    {
        var now = new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
        var doc = TaskDocument.CreateNew("T-0001", "First: a task", now);
        doc.AddItem("Do the thing");
        doc.SetNotes("Line one\n# not a heading");
        doc.Park = new ParkNote(now, "State.\nNext step.");
        doc.AppendLog(now, "user", "created");
        var text = doc.Serialize();

        var reparsed = TaskDocument.Parse(text);
        Assert.Equal("First: a task", reparsed.Title);
        Assert.Equal("State.\nNext step.", reparsed.Park?.Note);
        Assert.Equal("Line one\n\\# not a heading", reparsed.Notes);
        Assert.Equal("Do the thing", Assert.Single(reparsed.Plan).Text);

        reparsed.Updated = now.AddMinutes(1);
        reparsed.Updated = now;
        Assert.Equal(text, reparsed.Serialize());
    }

    [Fact]
    public void A_new_task_body_has_the_documented_shape()
    {
        var now = new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
        var doc = TaskDocument.CreateNew("T-0001", "Shape", now);
        doc.AddItem("First");
        doc.AddItem("Second");
        doc.SetNotes("A note.");
        doc.AppendLog(now, "user", "created");

        var body = doc.Serialize().Split("---\n")[2];

        Assert.Equal(
            "\n## Plan\n- [ ] First {#p1}\n- [ ] Second {#p2}\n\n## Notes\nA note.\n\n## Log\n- 2026-11-25T09:00Z user created\n",
            body);
    }

    [Fact]
    public void Hand_typed_plan_lines_are_never_hidden()
    {
        var doc = TaskDocument.Parse("---\nid: T-0004\n---\n\n## Plan\nsomething something something\n- bare step\n* [x] starred and done\n\n## Notes\n");

        Assert.Equal("something something something", doc.PlanText);
        Assert.Equal(["bare step", "starred and done"], doc.Plan.Select(p => p.Text));
        Assert.False(doc.Plan[0].Done);
        Assert.True(doc.Plan[1].Done);

        doc.EnsureItemIds();
        Assert.Contains("- [ ] bare step {#p1}\n* [x] starred and done {#p2}", doc.Serialize(), StringComparison.Ordinal);
        Assert.True(doc.SetItemDone("p1", true));
        Assert.Contains("- [x] bare step {#p1}", doc.Serialize(), StringComparison.Ordinal);
    }

    // A later provider (sprig) records keys this version doesn't know; they must survive every write.
    private const string WithWorktrees = """
        ---
        id: T-0050
        title: Worktrees
        state: active
        repos:
          - C:\git\example-app
        worktrees:
          - path: C:\git\example-app.worktrees\t-0050-fix
            repo: C:\git\example-app
            provider: git-worktree
            branch: t-0050-fix
            created: 2026-11-26T10:00:00Z
          - path: C:\git\example-app.worktrees\slot-2
            repo: C:\git\example-app
            provider: sprig
            key: slot-2
          - note: hand-written, no path
        ---

        ## Plan

        ## Notes

        ## Log

        """;

    [Fact]
    public void Worktrees_are_read_and_an_unchanged_file_stays_byte_identical()
    {
        var text = Lf(WithWorktrees);
        var doc = TaskDocument.Parse(text);

        Assert.Equal(
            [
                new TaskWorktree(@"C:\git\example-app.worktrees\t-0050-fix", @"C:\git\example-app", "git-worktree", "t-0050-fix", new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero)),
                new TaskWorktree(@"C:\git\example-app.worktrees\slot-2", @"C:\git\example-app", "sprig", null, null),
            ],
            doc.Worktrees);
        Assert.Equal(2, doc.ToSummary("T-0050").Worktrees.Count);
        Assert.Equal(text, doc.Serialize());

        // Re-emitted frontmatter keeps the documented layout too.
        doc.Updated = new DateTimeOffset(2026, 11, 26, 11, 0, 0, TimeSpan.Zero);
        Assert.Contains("    provider: sprig\n    key: slot-2\n  - note: hand-written, no path\n", doc.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_and_removing_a_worktree_leaves_the_other_entries_exactly_as_they_were()
    {
        var doc = TaskDocument.Parse(Lf(WithWorktrees));
        var created = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

        doc.AddWorktree(new TaskWorktree(@"C:\git\example-app.worktrees\t-0050-more", @"C:\git\example-app", "git-worktree", "t-0050-more", created));
        var added = doc.Serialize();
        Assert.Contains(
            "  - path: C:\\git\\example-app.worktrees\\t-0050-more\n    repo: C:\\git\\example-app\n    provider: git-worktree\n    branch: t-0050-more\n    created: 2026-11-26T12:00:00Z\n---",
            added,
            StringComparison.Ordinal);

        var reparsed = TaskDocument.Parse(added);
        Assert.Equal(3, reparsed.Worktrees.Count);
        // Paths compare without case, as Windows folders do.
        Assert.True(reparsed.RemoveWorktree(@"c:\git\example-app.worktrees\T-0050-FIX"));
        Assert.False(reparsed.RemoveWorktree(@"C:\nowhere"));
        var removed = reparsed.Serialize();

        Assert.DoesNotContain("t-0050-fix", removed, StringComparison.Ordinal);
        Assert.Contains("    provider: sprig\n    key: slot-2\n  - note: hand-written, no path\n  - path:", removed, StringComparison.Ordinal);
        Assert.Equal(["sprig", "git-worktree"], TaskDocument.Parse(removed).Worktrees.Select(w => w.Provider));
    }

    [Fact]
    public void Removing_the_last_worktree_drops_the_key()
    {
        var doc = TaskDocument.CreateNew("T-0001", "One", new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
        doc.AddWorktree(new TaskWorktree(@"C:\w\a", @"C:\r", "git-worktree", "a", null));
        Assert.Contains("worktrees:", doc.Serialize(), StringComparison.Ordinal);

        Assert.True(doc.RemoveWorktree(@"C:\w\a"));
        Assert.DoesNotContain("worktrees", doc.Serialize(), StringComparison.Ordinal);
        Assert.Empty(doc.Worktrees);
    }

    [Theory]
    [InlineData("no frontmatter")]
    [InlineData("---\nid: T-1\n")]
    [InlineData("---\n- a list\n---\n")]
    [InlineData("---\nid: [unclosed\n---\n")]
    public void Malformed_files_throw_a_format_exception(string text) =>
        Assert.Throws<TaskFormatException>(() => TaskDocument.Parse(text));
}
