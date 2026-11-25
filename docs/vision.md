# Vision

## The problem

Claude Code sessions act as the working memory for "what am I doing", and they are ephemeral. After a restart, a reboot or just a night's sleep, the picture of what was in flight, why and what came next is gone. IDEs are no longer the main surface either: the editor is opened only to read or tweak something small. What is missing is an environment built around **directing agents through tasks**.

One test for every feature: *does it help me know where to start tomorrow, and get back into it quickly?*

## Lesson from Perch

Perch shipped "work items" (grouping sessions into named pieces of work) and removed them because nobody used them. Grouping after the fact is homework. In Ochre the task is the **entry point**: agents are launched from it, it holds the plan, and the agents write to it. If a task isn't load-bearing, it won't be used.

## Requirements

### Core (MVP)

- **Task list.** Create, edit, order and filter tasks. Tasks are app-local, because a task can span several repos and no repo owns it.
- **Task lifecycle independent of agents.** Backlog, Active, Parked, Done. A task can run as a saga across many sessions and days, and you mark it done, not the agents.
- **Multiple agents per task.** Each agent is bound to the task, reads its plan and checks items off through the task CLI/MCP.
- **The task holds the plan**, not random markdown files scattered across repos.
- **Park & resume.** Parking a task asks its agents for a "state + next step" note, then leaves their sessions dormant. The morning view shows every Active and Parked task with its note, next step, attached sessions (one click to resume) and git state.
- **Perch-controlled sessions by default** (stream-json), fully interactive. Sessions started elsewhere (a terminal, an IDE) are observed **read-only** and can be bound to a task.
- **Background tray process** that keeps monitoring with no window open.

### Next

- **Jira import + link.** A task references any number of Jira issues (and PRs, Confluence pages, docs, other tasks). It acts as a **data-source hub**: agents get an index of the links and fetch detail on demand.
- **Integrated tabbed terminal** for running anything else in the app, restoring tabs, cwd, scrollback and launch command when reopened, and staying alive while the tray runs.
- **Git worktree + graph** support: see what is being edited across all tasks right now.
- **sprig** (optional module): worktree slots, quick-start apps, logs and status.
- **Multiple windows across monitors**, each a saved layout, with a global "jump to task" switcher.
- **Roost-style grid** of live sessions for a task, or across tasks.
- **Attention inbox**: one queue of everything that needs you (permission prompts, questions, finished turns, failed CI), grouped by task.

### Later

- **File editor** (good, not an IDE): syntax highlighting, find, diff against HEAD, markdown preview.
- **User-defined widgets** (like Claude Code's status line and mods), on the same system as plugins.
- **Plugins**, with capability manifests, consent and sandboxing.
- **Preview pane** (an embedded browser scoped to previews): sprig apps next to their logs, drivable by agents.
- Per-task usage, cost and time.
- Multi-repo tasks surfaced first-class in the git views.
- Other agents (Codex, Gemini) behind the agent adapter seam.
- macOS head.

## Non-goals

- Replacing an IDE. No language servers or refactoring tools in the core.
- A general-purpose browser (tabs, extensions, passwords). Links open in the real browser; middle click opens a new browser window.
- Two-way Jira sync.
- Requiring sprig, Hypertree, flamingle, Perch or any particular workflow.
- Cloud-hosted agents. Ochre drives local Claude Code.
