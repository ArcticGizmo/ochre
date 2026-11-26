# M4 slice 5: git graph

What changed: the Git module adds a **Git** tab to the title bar with a read-only commit graph of a task folder's repo, and a **Graph** link beside each git folder on a task. A repo's link opens every branch; a worktree's opens **This branch**: that branch against the one it was made from, back to where they split. Each window keeps its own repo and view.

The view has a refs rail (worktrees, branches with ahead/behind their upstream, remote branches, tags, stashes, each with a count), the graph (a dashed row per worktree with uncommitted changes, stashes woven in where they were made, branch, remote, tag and worktree pills, the folder's own work down the left), a find box, and the selected row's details on the right: a commit's full hash with Copy, author and email, date, parents and whole message; a worktree's uncommitted files; a stash's message and the commit it was made on. It loads 2,000 commits at a time. Nothing changes a repo. History refreshes when the window takes focus, after an agent's turn on the task, and from the refresh button; never on a timer.

Run the dev host headless to drive it (`$env:OCHRE_DEV_HEADLESS='1'; .\run.cmd`), or normally to try it yourself. The design is in `docs/design/m4-graph/`.

## Checks

| Try | Before | After |
|---|---|---|
| Open a task whose repo is a git checkout | Repos shows the branch and changes | A **Graph** link (branch icon) follows them |
| Click **Graph** on a repo | | The **Git** tab opens on **All**: the repo's name and path in the picker, its history with the checked-out branch in lane 0 down the left |
| Leave uncommitted changes in the repo | | The top row reads "N uncommitted changes" with the worktree's name, joined to its commit by a dashed line; it's selected, and the details list the files with M / A / D / R / U / ? |
| Click a commit | | Details: subject, the branches and tags on it, full hash, author and email, date, parents (click one to jump to it), and the rest of the message, with hard-wrapped lines rejoined (lists and indented lines kept) |
| **Copy** beside the hash | | Reads "Copied" for a moment; the hash is on the clipboard |
| A merge commit | | A larger node; its second parent's line opens in the band below it and rejoins further down |
| Make a stash (`git stash push -m "try"`), then refresh | | A dashed square row "try" with a `stash@{0}` pill sits by when it was made, tethered to its commit; the rail lists it under Stashes |
| Type in **Find** (a word, an author, a branch name, or 4+ hex characters) | | "1 of N"; Enter goes to the next match, Shift+Enter back; matches are tinted. Esc clears it |
| Click an item in the rail | | Selects and scrolls to it; one not in the loaded history is greyed out |
| Arrow keys in the list | | Move the selection up and down |
| **Worktrees** | | Only the trunk and what each worktree has checked out |
| Click **Graph** on a task's worktree | | Opens on **This branch**: "against origin/main" (or the best trunk), a strip with the task, "↑N ahead of origin/main", "↓N behind" and the uncommitted count |
| Pick another base, or "Nothing: its whole history" | | The history narrows to that base, or shows the branch's whole history |
| A repo with more than 2,000 commits | | "2,000 commits loaded" and **Load 2,000 more** at the bottom; the next page continues the lines without a jump |
| The repo picker | | Lists every task's repos and worktrees, open tasks first; picking a worktree opens This branch |
| Commit in a terminal, then click back into the window | | The graph refreshes (at most every 5 seconds on focus), keeping the selected commit |
| A task folder that isn't a git checkout | | "This folder isn't in a git checkout." |
| Turn the Git module off and restart | | No Git tab and no Graph links |

Automated: `GitGraphTests` (parsers, lane layout, snapshot, find, base-ref ranking; ported from Perch) and `GraphApiTests` (a graph loads only for one of the task's own folders; a base branch must be one git listed, so an option like `--output=` never reaches git; commit details and changes only for commits and worktrees the graph showed; an unknown token is refused; the commit parser).
