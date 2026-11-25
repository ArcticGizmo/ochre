# Architecture

Status: proposed. The UI technology is settled by the M0 spike ([spike-stack.md](spike-stack.md), [ADR 0002](decisions/0002-stack-dotnet-host-web-ui.md)).

## Overview

```
┌────────────────────────── nidus (tray / host process, .NET 10) ──────────────────────────┐
│                                                                                          │
│  Kernel                                  Modules (each can be turned off)                │
│  ├─ Task store (markdown files)          ├─ Git graph / worktrees                        │
│  ├─ Session registry                     ├─ Jira (TaskSource + link fetcher)             │
│  │   ├─ controlled (stream-json)         ├─ sprig (WorktreeProvider + panels)            │
│  │   └─ observed (read-only)             ├─ Terminal tabs (ConPTY)                       │
│  ├─ Workspace + WorktreeProvider         ├─ Roost grid                                   │
│  ├─ Event bus                            ├─ Usage / cost                                 │
│  ├─ Module host                          ├─ Editor, Widgets, Preview pane (later)        │
│  ├─ Window / layout manager              └─ Third-party plugins (out of process)         │
│  └─ Agent surface: CLI + MCP (named pipe)                                                │
│                                                                                          │
└───────────────▲───────────────────────────────────────▲──────────────────────────────────┘
                │ typed bridge (validated on host)        │ named pipe (ACL'd)
      ┌─────────┴──────────┐                    ┌─────────┴──────────┐
      │ Windows (WebView)  │  × N, any monitor  │ nidus CLI / MCP    │ ← used by agents
      │ web UI: tasks,     │                    │ (NativeAOT shim)   │
      │ Monaco, xterm.js   │                    └────────────────────┘
      └────────────────────┘
```

## Process model

- **One host process** (`nidus`), single-instance, with a tray icon. It owns everything stateful: the task store, controlled Claude sessions, terminal PTYs, module services, file watchers.
- **Windows are views.** Closing every window leaves agents and terminals running; they only stop when the app exits. That gives terminal persistence across window closes for free.
- The host keeps its services behind an in-process service boundary so it *could* split into a separate process later (crash isolation). Don't split until there's a reason.
- **CLI/MCP shim** (`nidus.com` / `nidus` NativeAOT, the Perch `perch.com` pattern) talks to the host over a named pipe. Agents use it to read and update their task.

## Kernel

The kernel must work with **zero modules enabled**: plain repos, no worktrees, no Jira, no terminal.

| Part | Responsibility |
|---|---|
| Task store | Markdown + frontmatter files under the app data dir; single writer (the host); file watcher picks up external edits. See [task-model.md](task-model.md). |
| Session registry | Every Claude Code session Nidus knows about: **controlled** (launched by Nidus over stream-json, fully interactive) or **observed** (read from `~/.claude/sessions/*.json` + transcripts, read-only). Bindings to tasks. Dormant sessions (resume on first send). |
| Workspace | Known repos, plus the active **WorktreeProvider**: `none` (just a cwd), `git-worktree`, or `sprig` (module). |
| Event bus | Session state changes, task changes, git changes, module events. |
| Module host | Loads modules, resolves their contributions, enforces on/off. |
| Window/layout manager | N top-level windows, each a saved layout of panels, with per-monitor placement (corner-relative, as Perch's `PlacementMath`). |
| Agent surface | `nidus task …` CLI and an MCP server exposing the same operations. |

### Observing sessions without a hook

Nidus starts **hook-free** ([ADR 0006](decisions/0006-hook-free-session-observation.md)): it reads Claude's own files (`sessions/{pid}.json`, transcripts, `history.jsonl`) as Perch does after its hook slimming. A Nidus hook binary is added only if a real gap appears. Perch's hook launches a process named `perch`, so Nidus must never depend on it.

## Modules and extension points

First-party features are modules built against the same contracts as plugins. Turning a feature off means turning its module off: no scattered `if (settings.XEnabled)`.

| Extension point | Purpose | First implementations |
|---|---|---|
| `TaskSource` | Import items as tasks or links | Jira; later GitHub Issues, Linear |
| `LinkResolver` | Turn a link into a title, summary and on-demand detail for agents | Jira, GitHub PR, URL, file |
| `WorktreeProvider` | Create/list/remove isolated checkouts | git worktree, sprig |
| `SessionLauncher` | Start an agent or process | Nidus-controlled Claude, terminal tab, Windows Terminal, flamingle layout |
| `AgentAdapter` | Launch/observe a kind of agent | Claude Code only for now ([ADR 0004](decisions/0004-claude-code-only.md)) |
| `Panel` | A view inside a window | git graph, Roost grid, sprig logs, usage, Hypertree map |
| `TaskAction` | A command on a task | Implement with agent, Park, Open PR, Transition in Jira |
| `Signal` | A badge/state on a task or session | CI state, PR review, app health |
| `EventHandler` | React to events | auto-park on OS shutdown, notify on task done |
| `Widget` | User-defined small display | see below |

## UI

**Proposed: one web UI inside a WebView hosted by the .NET process** (WebView2 on Windows, WKWebView on macOS). The candidate shells are Avalonia (`NativeWebView` + `TrayIcon`), Photino.NET, or raw WebView2; the spike picks one.

- Front end: TypeScript, React, Vite; pnpm with install scripts disabled, every dependency pinned.
- **Monaco** for the editor and diffs, **xterm.js** for terminals (with `addon-serialize` for scrollback restore).
- Assets are served from disk through a virtual host mapping (no local HTTP server for the UI itself).
- **The renderer is untrusted.** It talks to the host only through a narrow typed bridge that the host validates. No host objects are injected into script. See [security.md](security.md).
- Throughput-heavy streams (terminal output, transcript tails) may need a faster channel than `postMessage`; the spike measures that.

## Terminal

- ConPTY owned by the host; bytes streamed to xterm.js.
- **Restore** (first): tabs, cwd, scrollback and launch command persist; on reopen the shell is relaunched with the old scrollback shown.
- **Persist** comes free while the tray runs (the host owns the PTYs); nothing survives a reboot.
- Gate dangerous escape sequences (OSC 52 clipboard write, OSC 8 links with odd schemes).

## Widgets and plugins

One system, two trust tiers, one manifest format, one renderer:

- **Declarative widgets:** a template language (like Perch's statusline designer) over data the host already exposes. No consent needed.
- **Script widgets / plugins:** a command run out of process that emits JSON on an interval or on events. Requires consent for the capabilities in its manifest; hash-verified install; resource limits (Job Object on Windows). Plugin UI, when allowed, runs in a sandboxed iframe (opaque origin, strict CSP, `postMessage` only).

Perch's `pluggability-plan` branch has a working out-of-process host (manifest, NDJSON protocol, consent, Job Object sandbox) to copy; see [reuse-from-perch.md](reuse-from-perch.md).

## Preview pane (later)

An embedded WebView in a **separate profile with no bridge**, scoped to previews (sprig apps, PR pages). Agents may drive it through CDP, opened on demand on a random port with a throwaway profile.

## Data locations

| Data | Windows | macOS |
|---|---|---|
| Tasks | `%APPDATA%\Nidus\tasks\` | `~/Library/Application Support/Nidus/tasks/` |
| Settings | `%APPDATA%\Nidus\settings.json` | `…/Nidus/settings.json` |
| Terminal state | `%LOCALAPPDATA%\Nidus\terminals\` (encrypted scrollback) | `~/Library/Caches/Nidus/terminals/` |
| Secrets | Credential Manager (DPAPI) | Keychain |
| Logs | `%LOCALAPPDATA%\Nidus\logs\` | `~/Library/Logs/Nidus/` |

## Glossary

- **Task**: the durable unit of work; a markdown file with a plan, links and history.
- **Controlled session**: a Claude Code session Nidus launched over stream-json; interactive.
- **Observed session**: a session started elsewhere; read-only in Nidus.
- **Dormant session**: a session shown with its conversation but not running; it resumes when you send something.
- **Binding**: the link between a session and a task.
- **Park**: pause a task, collecting a "state + next step" note from its agents.
