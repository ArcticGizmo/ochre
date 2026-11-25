# Roadmap

Each milestone ends with a manual testing doc (`docs/testing/<milestone>-testing.md`) and a green build. Order may change; M0 must come first.

## M0: stack spike

Settle the UI technology and shell. See [spike-stack.md](spike-stack.md).

**Exit:** ADR 0002 accepted or replaced, with measurements recorded.

## M1: tasks and controlled sessions

- Solution skeleton: host (tray, single-instance), web UI, CLI shim, Core library, tests.
- Task store (format in [task-model.md](task-model.md)), atomic writes, watcher, log.
- Task list + task detail window.
- Start a controlled Claude session **from a task** (port Perch's `ClaudeSessionController` + stream-json parser), with `NIDUS_TASK` binding.
- Session thread view for controlled sessions.
- `nidus task show/check/add-step/note` over a named pipe.

**Exit:** create a task, launch two agents on it, both tick plan items, quit and relaunch, and the task is intact.

## M2: park, resume and the morning view

- Park (manual), collecting park notes from running sessions; dormant sessions; wake on send.
- "Today" view.
- Observed sessions (read-only) from `~/.claude/sessions` + transcripts; binding suggestions.
- Optional auto-park on OS shutdown (port Perch's `SessionLedger` / shutdown stamp logic).
- MCP server mirroring the CLI.

**Exit:** park two tasks at night; next morning "Today" shows both with notes; one click resumes a session with its context.

## M3: terminal tabs

- ConPTY in the host, xterm.js tabs, restore (tabs, cwd, scrollback, command).
- Terminals attachable to a task.
- Escape-sequence gating.

**Exit:** run a dev server in a tab, close all windows, reopen: still running. Quit and relaunch: tabs restored with scrollback.

## M4: module host and first modules

- Module host + settings to turn modules on and off.
- Move existing features behind modules where they belong.
- Git graph + worktrees module (port Perch's `GitGraph` model and `GitRunner`).
- Jira module: import + link, `LinkResolver`, `task fetch`.
- sprig module: `WorktreeProvider`, app status and logs panel.
- Multi-window layouts with per-monitor placement; "jump to task" hotkey.

**Exit:** turn every module off and the kernel still works; turn sprig on and a task's worktree is a sprig slot.

## M5: attention inbox and Roost grid

- Inbox of everything needing you, grouped by task.
- Roost-style grid of a task's sessions (interactive if controlled, read-only if observed).

## M6: editor

- Monaco: open/edit files from a task's repos, diff against HEAD, markdown preview.

## M7: widgets and plugins

- Declarative widgets.
- Out-of-process plugin host (port from Perch's `pluggability-plan` branch): manifest, consent, Job Object sandbox, sandboxed iframe UI.

## M8: preview pane

- Isolated WebView preview for sprig apps and PR pages; CDP on demand for agents.

## Later

- Per-task usage/cost/time.
- Other agents behind `AgentAdapter`.
- macOS head.
