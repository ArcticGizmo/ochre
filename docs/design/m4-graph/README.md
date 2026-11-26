# Design: git graph (M4 slice 5)

Agreed 2026-11-26, before the UI was changed (`docs/ux-conventions.md`, Process), with the commit message and a worktree's changed files in the details as drawn. Published as a private design canvas at https://claude.ai/artifact/HxGvxj3MDUWvY7dhhaTp7p; this folder is its local copy. Ochre's own colours, as in `docs/design/m3-terminals/`, rather than a design system; the lane colours are Perch's dark set.

Artboards in `project/`: `Main.dc.html` (the git module's **Git** view on all branches: refs rail, graph with a dirty worktree, a merge, a stash and a tag, find, the selected commit's detail and "Load 2,000 more"), `Branch.dc.html` (**This branch**: a task's worktree against its base, opened from the task, with its uncommitted files selected) and `Task.dc.html` (the task's Repos section, where **Graph** beside each folder opens the view). They render only inside the canvas, which supplies `support.js`. Sample repo, people, commits and paths are made up.

Read-only: no checkout, merge, rebase or other git actions, and no avatars.
