# Ochre

A task-first agent development environment (ADE) for Claude Code: a .NET 10 tray host plus a web UI in WebView2, hosted in a minimal WinForms shell on Windows (`docs/decisions/0002-stack-dotnet-host-web-ui.md`). The durable unit of work is the **task**; sessions, worktrees, terminals and links attach to it.

**Status: M0 (stack spike), M1 (tasks and controlled sessions), M2 (park, resume and Today) and M3 (terminal tabs) done; M4 (modules) in progress: slices 1 (module host), 2 (git module and worktrees), 3 (sprig module) 4 (multiple windows), 5 (git graph) and 7 (exit run: criterion met) done; slice 6 (Jira module, needs a Jira site) and the "jump to task" hotkey still to do.** Read `README.md`, then `docs/vision.md`, `docs/architecture.md`, `docs/task-model.md`, `docs/roadmap.md` and `docs/implementation-plan.md` before writing code. Spike findings that M1 must carry over are in `docs/spike-stack-results.md`; the spike code is at the git tag `spike-stack`.

## Layout

- `src/Ochre.Core/`: UI-free core (task store, session registry, transcript parsing, git, module contracts). Plain `net10.0`.
- `src/Ochre.Host/`: the tray host process (`ochre`), WebView shell, bridge, module host, PTYs.
- `src/Ochre.Cli/`: NativeAOT CLI/MCP shim that talks to the host over a named pipe.
- `src/Ochre.Platform.Windows/` (later `.Mac/`): OS implementations of Core interfaces.
- `ui/`: TypeScript + React + Vite web UI (Monaco, xterm.js).
- `modules/`: first-party modules (git, jira, sprig, terminal, …).
- `tests/Ochre.Tests/`: xUnit over Core, with fixtures.
- `spikes/`: throwaway experiments; never referenced by `src/`.
- `docs/`: vision, architecture, decisions (ADRs), roadmap, testing docs.

## Principles (enforce these in review)

- **Tasks first.** Agents are launched from tasks; the task holds the plan.
- **Compose.** The kernel works with zero modules. A feature is a module on the plugin contracts (ADR 0007), not a settings flag scattered through the code.
- **Explicit over magic.** Detection suggests; the user confirms. Never auto-bind, auto-add or auto-start.
- **Prefer simple over clever.** Recommend the small, direct fix and state its cost.
- **The renderer is untrusted.** All UI → host calls go through the typed bridge and are validated on the host. Never inject host objects into script. Sanitise any HTML/markdown. See `docs/security.md`.
- **The host is the only writer** of task files.

## Conventions

- Warnings are errors (`TreatWarningsAsErrors` in `Directory.Build.props`); fix warnings, don't suppress them.
- `Nullable` and `ImplicitUsings` on everywhere.
- IO and heavy work run off the UI thread; guard callbacks against closed windows.
- Never assume one window: several windows, each on its own task and virtual desktop, are a firm requirement (`docs/roadmap.md`, "Multiple windows"). Keep per-window state in the page and broadcast host events to every window.
- Read Claude's live files with `FileShare.ReadWrite` and tolerate partial trailing lines; never throw out of a scan.
- Every OS-specific capability goes behind a Core interface with a platform implementation.
- UI work follows `docs/ux-conventions.md` (dialogs for creates and terminal actions, counts on filters, status colours, centred content). Larger visual changes start from a design artefact.
- A file path in a one-line space keeps the **file name** and elides the leading directory.
- Every browser link also takes a middle click, which opens it in a new browser window.
- Secrets go in Credential Manager / Keychain, never in settings or logs.
- UI dependencies: exact pinned versions, lockfile committed, pnpm with install scripts disabled. Never run `npx`/`pnpm dlx` against unpinned packages.
- Release scripts (`.ps1`, `.bat`, `.sh`, workflows) stay pure ASCII; `.sh` files stay LF.
- Tests never plant runnable executables (EDR flags them); plant empty files and assert resolution.
- After changing a bridge record or `BridgeContract`, run `dotnet run --project tools/Ochre.BridgeGen` and commit `ui/src/generated/bridge-types.ts` (ADR 0009).
- `run.cmd` builds everything and starts the dev host; `cli.cmd <args>` builds and runs the console CLI against it (`cli status`, `cli quit`, ...). Both use the dev profile.
- Check UI changes in the real window, not just the build: set `OCHRE_DEV_HEADLESS=1` (the window opens off-screen, with no taskbar button or focus; Debug builds only) and `OCHRE_DEV_DESKTOP=<virtual desktop guid>` before `run.cmd`, so nothing lands on the user's screen, then drive it with `cli dev eval "<js>"` and look with `cli dev capture captures/<timestamp>-<reason>.png`. With several windows open, `cli dev windows` lists them and `--window <id>` picks one for `dev eval` and `dev capture`. From PowerShell or bash, call the scripts as `.\run.cmd` / `.\cli.cmd`.
- Experimental phase: write automated tests only for data-loss and security boundaries, the controlled-session lifecycle (in-memory `claude` via `tests/Ochre.Tests/FakeClaude.cs`; never start a real process or read `~/.claude` in a test), or once a feature's direction has settled. Code copied from Perch brings its existing tests. Everything else is verified by hand through the manual testing docs. See `docs/implementation-plan.md`.

## Copying from Perch

Allowed and encouraged (ADR 0003). Copy, then own. Put the source repo, path and commit in the commit message, and bring the matching tests and fixtures. Candidates: `docs/reuse-from-perch.md`.

## Decisions and docs

- Significant choices get an ADR in `docs/decisions/` (append-only; supersede rather than edit).
- Every change ships a manual testing doc at `docs/testing/<feature>-testing.md` with before/after prompts to try.
- Keep `docs/roadmap.md` current when a milestone's scope changes.
- In markdown, never insert line breaks in the middle of a sentence.

## Comments

Explain only the *why* behind non-obvious code. Never describe what the code used to do.
