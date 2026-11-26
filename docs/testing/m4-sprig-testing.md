# M4 slice 3: sprig module

What changed: a new **Sprig** module (off by default; turn it on in Settings and restart). With it on, a task's new worktree can be a **sprig slot**: the start dialogs' "A new worktree for this task..." gains a Kind choice (Git worktree or Sprig slot), and the task remembers the kind it used last. A slot is made with `sprig checkout -b <branch>` (nothing starts), recorded on the task with its workspace key, and given back with `sprig free` from the same Remove dialog, now worded "Give slot back". Every sprig workspace a task's repos or worktrees are in shows on the task and on Today: its state beside the folder, and an **Apps** section with each app's state and port, Start apps / Restart / Stop, Open in sprig, and each app's log. All of it comes from one `sprig watch --json` the host runs while the module is on; nothing polls and nothing starts by itself.

Also in this slice: the git primitives (`GitRunner`, status, worktree scanning, branch checks) moved from the git module into Core (`src/Ochre.Core/Git/`), so both providers share them.

Run the dev host headless so nothing lands on your screen: `$env:OCHRE_DEV_HEADLESS='1'; .\run.cmd`.

## Checks

| Try | Before | After |
|---|---|---|
| Settings: tick **Sprig**, restart | No sprig module | `app.info` lists `sprig`; with both git and sprig on, "A new worktree for this task..." shows a **Kind** select |
| New worktree, Kind: Sprig slot, on a repo sprig doesn't know | | "this folder isn't on the Repos list. Run sprig init here, or name a repo or orchestration ID" under the branch, before anything is made |
| The same on a repo whose `sprig.yaml` isn't approved | | Sprig's message with `(untrusted)` and "Open sprig to sort it out, then try again". Ochre never approves a definition |
| On a repo sprig knows | | "New branch from the repo's default branch, in a sprig slot it picks" (or the slot's folder, when the branch is in one already) |
| Start an agent (or open a terminal) in a new sprig slot | | The slot is made (`<repo>--<n>`), the task lists it as a worktree with the git status and **sprig stopped** beside it, and the task log says `added worktree ... (sprig)`. Nothing is running |
| Look at the task's **Apps** section | | The workspace's branch and key, its apps with state (off), **Start apps** and **Open in sprig** |
| **Start apps** | | Within a moment the workspace shows starting (blue), then running (green) with `1/1` beside the folder; each app shows ready and its port, which opens `http://localhost:<port>` (middle click: a new window) |
| An app's **Log** | | The end of the app's log file (the last 300 lines), newest at the bottom; Refresh reads it again |
| **Restart** / **Stop** | | The workspace goes blue while apps restart or stop, then green or grey |
| Make an app crash | | The workspace turns red (trouble) and the app shows `exited <code>` and its error |
| A start that sprig refuses (Docker not running, inputs needed) | | The refusal under the workspace with sprig's code; for ones only sprig can sort out, a **Sort it out in sprig** link opens sprig's window on the workspace |
| Open Today | | The task card's Repos row shows the slot with its sprig state |
| A task whose repo is sprig's main checkout of something | | Its apps show too (the main workspace), with Start apps |
| **Remove...** on the slot with an uncommitted file in it | | Titled **Give slot back?**; says what sprig keeps; "1 uncommitted change in it. Commit or stash them first." and the button disabled (sprig refuses to free a dirty slot) |
| Give back a clean slot | | Sprig stops its apps and frees the slot; the folder stays as a free slot on a detached HEAD; the task entry goes; the branch is deleted only if ticked (ticked by default only when no commit is unique to it) |
| Free the slot in sprig's own window, then Remove in Ochre | | The dialog says there's nothing left of the task in it, and only tidies up |
| Remove right after the host starts, before sprig has reported | | "Ochre hasn't heard from sprig yet; try again in a moment"; the slot is never skipped over |
| Uninstall or rename sprig, restart Ochre | | Apps says "Sprig isn't installed"; Ochre looks again every minute |
| Quit sprig's host while apps run | | Apps says sprig's host isn't running; starting an app brings it back |
| Turn sprig off, restart | | No Apps section or sprig badge; the slot is a plain folder agents can start in; Kind select gone with only git on |

Automated: `SprigStatusTests` (the watch stream fold, readiness, tolerant parsing, action arguments and command results, all ported from Perch; plus task-folder matching, the `checkout --json` answer, pretty-printed failures, the log tail, and refusing to give a slot back before sprig has reported), the `key` round trip in `TaskDocumentTests`, and everything from slice 2 against the moved Core git code.
