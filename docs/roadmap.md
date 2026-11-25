# Roadmap

Each milestone ends with a manual testing doc (`docs/testing/<milestone>-testing.md`) and a green build. Order may change; M0 must come first.

## M0: stack spike (done 2026-11-25)

Settle the UI technology and shell. See [spike-stack.md](spike-stack.md).

**Exit:** ADR 0002 accepted or replaced, with measurements recorded. Met: ADR 0002 accepted with raw WebView2 in a minimal WinForms host; numbers in [spike-stack-results.md](spike-stack-results.md).

## M1: tasks and controlled sessions (done 2026-11-25)

- Solution skeleton: host (tray, single-instance), web UI, CLI shim, Core library, tests.
- Task store (format in [task-model.md](task-model.md)), atomic writes, watcher, log.
- Task list + task detail window.
- Start a controlled Claude session **from a task** (port Perch's `ClaudeSessionController` + stream-json parser), with `OCHRE_TASK` binding.
- Session thread view for controlled sessions.
- `ochre task show/check/add-step/note` over a named pipe.

**Exit:** create a task, launch two agents on it, both tick plan items, quit and relaunch, and the task is intact. Met: see [testing/m1-testing.md](testing/m1-testing.md). Agents run the task commands without a permission prompt ([ADR 0010](decisions/0010-pre-allow-task-commands.md)); their plan changes are highlighted until reviewed.

## M2: park, resume and the morning view

- Park (manual), collecting park notes from running sessions; dormant sessions; wake on send.
- "Today" view.
- Observed sessions (read-only) from `~/.claude/sessions` + transcripts; binding suggestions.
- Optional auto-park on OS shutdown (port Perch's `SessionLedger` / shutdown stamp logic).
- Observed sessions bound to a task are read-only; only agents Ochre launched write to tasks.
- The MCP server mirroring the CLI moved to Later (2026-11-25): agents use the CLI reliably, and it's cheaper and simpler.

**Exit:** park two tasks at night; next morning "Today" shows both with notes; one click resumes a session with its context.

## M3: terminal tabs

- ConPTY in the host, xterm.js tabs, restore (tabs, cwd, scrollback, command).
- Terminals attachable to a task.
- Escape-sequence gating.
- To look at (2026-11-25): a "view as terminal" option on an agent's thread, drawing the same conversation in Claude Code's terminal style. A real Claude TUI in a tab can't drive the same session at once, because Ochre holds it over stream-json, so this is a rendering of the thread rather than a second client.

**Exit:** run a dev server in a tab, close all windows, reopen: still running. Quit and relaunch: tabs restored with scrollback.

## M4: module host and first modules

- Module host + settings to turn modules on and off.
- Move existing features behind modules where they belong.
- Git graph + worktrees module (port Perch's `GitGraph` model and `GitRunner`).
- Jira module: import + link, `LinkResolver`, `task fetch`.
- sprig module: `WorktreeProvider`, app status and logs panel.
- Multiple windows, each showing a specific task (or the task list), so tasks can live on specific virtual desktops; see "Multiple windows" below. Per-monitor placement; "jump to task" hotkey.

**Exit:** turn every module off and the kernel still works; turn sprig on and a task's worktree is a sprig slot; two tasks open in two windows on two virtual desktops come back on those desktops after quitting and relaunching.

### Multiple windows (requirement, 2026-11-25)

The user keeps specific tasks on specific virtual desktops, so Ochre must support several windows at once:

- Open any task in its own window (from the list, a context menu, or `ochre open T-0042`). The new window opens on the **current** virtual desktop.
- Each window remembers its task, size, position and virtual desktop. On relaunch, windows reopen on the desktop they were on (Windows lets a process move its own windows, so this is possible).
- Opening a task that's already open in a window on another desktop asks: bring that window here, or open another view here. It never silently switches desktops.
- Closing a window closes only that view; the host and other windows carry on (ADR 0008).

Until then, don't build anything that assumes a single window: per-window UI state (selected task, scroll) lives in the page, and host events go to every window (WindowHost already broadcasts).

## M5: attention inbox and Roost grid

- Inbox of everything needing you, grouped by task.
- Roost-style grid of a task's sessions (interactive if controlled, read-only if observed).

## M6: editor

- Monaco: open/edit files from a task's repos, diff against HEAD, markdown preview.

## M7: widgets and plugins

- Declarative widgets.
- User-defined status widgets in a pane to the right of an agent's thread (requested 2026-11-25, inspired by Claude mods).
- Out-of-process plugin host (port from Perch's `pluggability-plan` branch): manifest, consent, Job Object sandbox, sandboxed iframe UI.

## M8: preview pane

- Isolated WebView preview for sprig apps and PR pages; CDP on demand for agents.

## Later

- Per-task usage/cost/time.
- Plan usage (5-hour and weekly limits) in place of the API-equivalent cost, which means little on a Pro or Max plan. Perch reads it from Claude's OAuth usage endpoint (`UsageMonitor`), which needs the user's OAuth token, so it would be opt-in.
- MCP server mirroring the agent CLI, if a client that can't run a shell needs task access.
- Other agents behind `AgentAdapter`.
- macOS head.
