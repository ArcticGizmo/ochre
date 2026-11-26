# M4 exit run: modules, sprig slots and windows on desktops

Exit criterion (roadmap): turn every module off and the kernel still works; turn sprig on and a task's worktree is a sprig slot; two tasks open in two windows on two virtual desktops come back on those desktops after quitting and relaunching.

Walked 2026-11-26. The first two parts in the dev host, headless on the dev desktop, driven with `cli dev eval` and checked with `cli dev capture`; the third by the user, by hand, on two real desktops.

## Every module off

The dev profile had git, sprig and terminals on (shutdown-park off). All four were turned off in Settings and Ochre restarted itself; the choices were put back afterwards.

| Step | Result |
|---|---|
| Restart with every module off | `app.info` lists no modules; the log shows the restart and no bridge contract errors; the saved windows came back |
| Look at the title bar | Today, Tasks and Board only: no Terminals or Git |
| Create T-0013 with a repo (this checkout), add a plan step, tick it, make it Active | Each call answers; the task shows the step ticked, its notes and repo as a plain path (no git status, no Graph link), and no Terminals section |
| The outside-session suggestion | Still offered: a Claude session in the repo "looks related" (a kernel feature) |
| **Start agent** | Folder offers the repo and "Another folder..." only; no new-worktree option |
| Start a real agent on T-0013 with the prompt "Reply with the single word: ready" | It answered "ready"; the thread view, model and effort menus and Stop all there |
| Stop it | Status dormant |

## Sprig on: a task's worktree is a sprig slot

Done on InFlight.NextGen (a repo sprig already knows), with the user's go-ahead, on a throwaway branch `t-0013-exit-check` that was given back and deleted afterwards.

| Step | Result |
|---|---|
| Add InFlight.NextGen to T-0013; plan a new worktree, Kind: Sprig slot | "New branch from the repo's default branch, in a sprig slot it picks" |
| Make it (first try) | Sprig made slot `InFlight.NextGen--1` on the branch, then refused it: "the definitions in the workspace's worktrees have problems". Ochre reported the refusal but didn't record the slot, leaving a slot and branch nothing tracked (fixed below; the slot was freed and the branch deleted by hand) |
| Make it again, with the fix | The task records the worktree: provider `sprig`, key `InFlight.NextGen/1`, folder `InFlight.NextGen--1`, branch `t-0013-exit-check`. Repos shows it as a worktree with its git status, **sprig failed** beside it and a Graph link; Apps shows the slot failed with sprig's message, beside the main checkout's apps |
| Why sprig refused | `sprig info` in the slot: the repo's `sprig.yaml` exists only on `jon-sprig`, so a branch from the default branch has none. A property of that repo, not Ochre |
| **Remove...** on the slot | "Give slot back?", sprig's description of what it keeps, "Also delete the branch t-0013-exit-check" ticked ("Every commit on it is also on another branch or a remote") |
| Give slot back | The slot is free in sprig (its folder left on a detached HEAD, as sprig keeps free slots), the branch is gone, the task no longer lists it, and the main checkout is as it was |

## Two windows on two desktops

Checked by the user on two real virtual desktops (`m4-windows-testing.md`, exit criterion row): task windows on desktops 1 and 2 reopened on their own desktops after quitting and relaunching. The rest of the windows checks in that doc were run headless on the dev desktop in slice 4.

## Found on the way

- Sprig can make a slot and then refuse it. The sprig provider now looks for the slot sprig holds for the branch (its answer, else the watch, for up to 5 s) and records it, so the task shows sprig's error beside it and can give it back; before, the slot and branch were left with nothing tracking them. See the new row in `m4-sprig-testing.md`.

Per-slice checks: `m4-modules-testing.md`, `m4-git-testing.md`, `m4-sprig-testing.md`, `m4-windows-testing.md`, `m4-graph-testing.md`.

Afterwards (same day): the "jump to task" hotkey was built (`m4-jump-testing.md`), and the Jira module (slice 6) moved out of M4 to the roadmap's Later list.
