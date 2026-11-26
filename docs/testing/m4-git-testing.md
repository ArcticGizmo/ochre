# M4 slice 2: git module and worktrees

What changed: a new **Git** module (on by default) shows each task folder's branch, commits ahead and behind its upstream, and uncommitted changes, next to the folder on the task and on Today. It also offers **git worktrees**: "Start agent" and "Open terminal" can start in a new worktree for the task, on its own branch, laid out the way your other worktrees are (beside the repo in `<repo>.worktrees\` when you have none). The task records each worktree in a new `worktrees` list in its file, and a confirmed dialog removes one. With the module off, a task's repos and worktrees are plain folders and agents start in them as before.

Run the dev host headless so nothing lands on your screen: `$env:OCHRE_DEV_HEADLESS='1'; .\run.cmd`, then drive it with `ochre-cli dev eval` and `dev capture`. Or run it normally to try it yourself.

## Checks

| Try | Before | After |
|---|---|---|
| Open a task whose repo is a git checkout with an uncommitted file | Repos lists the path | The path is followed by the branch (`main`), `↑n`/`↓n` when it's ahead or behind its upstream, and `1 changed` in amber (or `clean` in green). Hovering shows the upstream and counts in words |
| Make it Active and open Today | No repos on the card | A Repos row with the same branch and changes for each repo and worktree |
| Commit or change a file in a terminal outside Ochre, then click back into the Ochre window | | The counts update when the window takes focus, not before (no timer) |
| Start an agent, let it change a file and finish its turn | | The task's counts update when the turn ends, without clicking anything |
| A repo that isn't a git checkout | | Just the path, no status |
| **Start agent**, Folder: **A new worktree for this task...** | Only repos and "Another folder" | A Branch field filled with `t-0042-<title>`; below it "New branch from main, in ...\web.worktrees\t-0042-..." (or "Checks out the existing branch in ..." for a branch that exists) |
| Type `bad name` as the branch | | A red line explaining what a branch name may hold; Start agent stays available but the host refuses it with the same reason |
| Type a branch already checked out (e.g. `main`) | | "main is already checked out in ..." |
| Start the agent in a new worktree | | The button reads "Making worktree..." briefly, then Ochre asks to trust the new folder (Claude Code trusts per folder), then the agent starts there. Repos lists it as **worktree** with its own branch and status; the task log says `added worktree ...` |
| **Open terminal** on the task, Folder: a new worktree | | Same fields; the terminal opens in the new worktree, tagged with the task |
| Start agent again | | The existing worktree is in the Folder list with its branch, next to the repos |
| In a repo whose existing worktrees sit at `..\web-<branch>`, make a new one | | It lands at `..\web-t-0042-...`, following your layout |
| On a worktree, **Remove...** with uncommitted changes and a commit only that branch has | | The dialog says the folder is deleted, offers "Discard 1 uncommitted change" (Remove is disabled until ticked) and "Also delete the branch", **unticked**, warning "1 commit on it exists on no other branch or remote" |
| Remove a clean worktree whose branch is merged or pushed | | "Also delete the branch" is ticked and says every commit is also elsewhere |
| Remove while an agent is running in the worktree | | "1 agent working in this folder. Stop them first." and Remove is disabled |
| Remove while a terminal is open in the worktree (Windows) | | git deletes the files but not the folder; the dialog says to close the program holding it and remove again. After closing the terminal, Remove finishes and the task entry goes; the branch stays unless you ticked it |
| Delete a worktree folder by hand, then Remove | | Repos shows "folder gone"; the dialog says it only tidies up, and git forgets the worktree |
| Ask an agent for `ochre task show` | | A Worktrees section lists each worktree path and branch |
| An outside Claude session running in a task's worktree | | It's suggested for that task ("folder is its worktree ..."), ahead of other tasks sharing the repo |
| Turn every module off in Settings and restart | | No status after any folder, no Terminals tab; Start agent offers the repos and the recorded worktrees as plain folders (no "new worktree" option); a worktree shows its recorded branch in grey and no Remove |

Automated: `GitRunnerTests` (argument hardening, repo-config probe, repo-supplied commands never run, git-lfs), `GitStatusTests` (porcelain v2 parser), `GitWorktreesTests` and `WorktreeLayoutTests` (all ported from Perch), `GitWorktreeProviderTests` (new worktree beside the repo, removal counts what it would lose, refuses uncommitted work without consent, keeps the branch unless asked, finishes a half-removed worktree but never deletes stray files), the `worktrees` round trips in `TaskDocumentTests`, the provider contribution in `ModuleHostTests`, and the worktree suggestion in `BindSuggestionsTests`.
