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

## M2: park, resume and the morning view (done 2026-11-25)

- Park (manual), collecting park notes from running sessions; dormant sessions; wake on send.
- "Today" view.
- Observed sessions (read-only) from `~/.claude/sessions` + transcripts; binding suggestions.
- Optional auto-park on OS shutdown (port Perch's `SessionLedger` / shutdown stamp logic).
- Observed sessions bound to a task are read-only; only agents Ochre launched write to tasks.
- The MCP server mirroring the CLI moved to Later (2026-11-25): agents use the CLI reliably, and it's cheaper and simpler.

**Exit:** park two tasks at night; next morning "Today" shows both with notes; one click resumes a session with its context.

## M3: terminal tabs (done 2026-11-26)

- ConPTY in the host, xterm.js tabs, restore (tabs, cwd, scrollback, command).
- Terminals attachable to a task.
- Escape-sequence gating.
- To look at (2026-11-25): a "view as terminal" option on an agent's thread, drawing the same conversation in Claude Code's terminal style. A real Claude TUI in a tab can't drive the same session at once, because Ochre holds it over stream-json, so this is a rendering of the thread rather than a second client. Not built in M3; waits for dogfooding to ask for it.

**Exit:** run a dev server in a tab, close all windows, reopen: still running. Quit and relaunch: tabs restored with scrollback. Met: see [testing/m3-testing.md](testing/m3-testing.md). The host owns terminals and filters their output ([ADR 0011](decisions/0011-host-owned-terminals.md)); scrollback is saved encrypted with DPAPI; a startup command is offered again after a restart, never rerun by itself.

## M4: module host and first modules

- Module host + settings to turn modules on and off.
- Move existing features behind modules where they belong.
- Git graph + worktrees module (port Perch's `GitGraph` model and `GitRunner`).
- sprig module: `WorktreeProvider`, app status and logs panel.
- Multiple windows, each showing a specific task (or the task list), so tasks can live on specific virtual desktops; see "Multiple windows" below. Per-monitor placement; "jump to task" hotkey.

**Exit:** turn every module off and the kernel still works; turn sprig on and a task's worktree is a sprig slot; two tasks open in two windows on two virtual desktops come back on those desktops after quitting and relaunching. Met 2026-11-26: see [testing/m4-testing.md](testing/m4-testing.md). The "jump to task" hotkey followed the same day ([testing/m4-jump-testing.md](testing/m4-jump-testing.md)), which completes M4. The Jira module moved to Later (2026-11-26, the user's call).

### Multiple windows (requirement, 2026-11-25)

The user keeps specific tasks on specific virtual desktops, so Ochre must support several windows at once:

- Open any task in its own window (from the list, a context menu, or `ochre open T-0042`). The new window opens on the **current** virtual desktop.
- Each window remembers its task, size, position and virtual desktop. On relaunch, windows reopen on the desktop they were on (Windows lets a process move its own windows, so this is possible).
- Opening a task that's already open in a window on another desktop asks: bring that window here, or open another view here. It never silently switches desktops.
- Closing a window closes only that view; the host and other windows carry on (ADR 0008).

Started early (2026-11-26, at the user's request): the tray brings forward a window on the current desktop, or opens a new one there; see [testing/window-per-desktop-testing.md](testing/window-per-desktop-testing.md). Done in M4 slice 4 (2026-11-26): task windows, the "bring it here or open another" question, and windows reopening on their desktops and monitors; see [testing/m4-windows-testing.md](testing/m4-windows-testing.md). The "jump to task" hotkey (Ctrl+Alt+Space by default, Ctrl+K inside a window) followed; see [testing/m4-jump-testing.md](testing/m4-jump-testing.md).

Don't build anything that assumes a single window: per-window UI state (selected task, scroll) lives in the page, and host events go to every window (WindowHost already broadcasts).

## M5: attention inbox and Roost grid (exit walked 2026-11-26; hand checks open)

- Inbox of everything needing you, grouped by task: permission prompts, questions, plan approvals, finished turns, observed sessions waiting.
- Roost-style grid of a task's sessions (interactive if controlled, read-only if observed).
- Rich desktop notifications for inbox items (grouped by task, with buttons and replies), starting with a spike.
- Scope set 2026-11-26 with the user: the grid shows one task's agents; a grid across tasks and failed CI in the inbox wait (Later); notifications are in. Plan in [implementation-plan.md](implementation-plan.md), M5; placement in [ADR 0013](decisions/0013-attention-inbox-and-roost.md).

**Exit:** three agents on a task stream side by side in its grid and a permission is answered in a pane; another window's inbox shows a question and a finished turn grouped by task, and answering or opening them clears them everywhere; an outside `claude` linked to the task shows read-only in the grid and in the inbox while it waits; a permission prompt pops a notification that can allow it, and answering inside Ochre takes the notification away; with the `roost` and `notifications` modules off, the inbox still works. Walked 2026-11-26 in a headless dev host: every step passed except the outside `claude` one, which is left for a hand run with real notifications; see [testing/m5-testing.md](testing/m5-testing.md).

## Distribution (2026-11-26)

- An installed Ochre, as Perch ships: `publish.ps1` packs a Velopack installer and update feed; a `v*` tag publishes a GitHub release (`release.yml`); `install.ps1` installs it; the app updates itself when asked; CI runs on pull requests. Added between M5 and M6 at the user's request, so Ochre can be used day to day. See [ADR 0014](decisions/0014-velopack-distribution.md) and [testing/release-testing.md](testing/release-testing.md).
- Later: code signing; a GitHub release for macOS when its head exists.

## M6: editor (planned 2026-11-26)

- Monaco: open/edit files from a task's repos, diff against HEAD, markdown preview.
- Scope set 2026-11-26 with the user: files are edited and saved; the editor is reached from a file tree, a task-wide Changes list, the git graph's commits, file paths in agents' tool cards, and a Ctrl+P picker. Plan in [implementation-plan.md](implementation-plan.md), M6; placement and file-access rules in [ADR 0015](decisions/0015-editor-module-and-file-access.md).

## M7: widgets and plugins

- Declarative widgets.
- User-defined status widgets in a pane to the right of an agent's thread (requested 2026-11-25, inspired by Claude mods).
- Out-of-process plugin host (port from Perch's `pluggability-plan` branch): manifest, consent, Job Object sandbox, sandboxed iframe UI.

## M8: preview pane

- Isolated WebView preview for sprig apps and PR pages; CDP on demand for agents.

## Later

- Jira module: import + link, `LinkResolver`, `task fetch` (moved out of M4 on 2026-11-26; its plan stays in `docs/implementation-plan.md`, M4 slice 6, and needs a Jira site to test against).
- GitHub module: failed CI, review requests and conflicts on a task's branch, as inbox items (moved out of M5 on 2026-11-26; Perch's `GitHubAlerts` is the reference).
- A Roost grid across tasks (the vision's "or across tasks"; M5 builds one task's grid).
- Per-task usage/cost/time.
- Plan usage (5-hour and weekly limits) in place of the API-equivalent cost, which means little on a Pro or Max plan. Perch reads it from Claude's OAuth usage endpoint (`UsageMonitor`), which needs the user's OAuth token, so it would be opt-in.
- MCP server mirroring the agent CLI, if a client that can't run a shell needs task access.
- Other agents behind `AgentAdapter`.
- macOS head.
