# Handover: M4 slice 2 (git module and worktrees)

Written 2026-11-26 at the end of a long session, for the next one to pick up cold. Branch `rename-to-ochre`, last commit `2104a39` (M4 slice 1). Nothing is pushed; the user asked for one commit per finished slice, never a push.

## Where things stand

- M0 to M3 are done. M4 is planned in detail in `docs/implementation-plan.md` (section "M4: module host and first modules"); read that section and [ADR 0012](../decisions/0012-module-contracts.md) first.
- **Slice 1 (module host) is done**: `IModule`/`IModuleContext`/`ModuleHost` in `src/Ochre.Core/Modules/`, terminals in `modules/Ochre.Modules.Terminals/`, auto-park in `modules/Ochre.Modules.ShutdownPark/`, UI registry in `ui/src/modules/`. Testing doc: `docs/testing/m4-modules-testing.md`.
- Also done this session, outside the plan: the tray opens a window on the current virtual desktop (`14b819e`, an early piece of slice 4); restored terminals reopen in the folder the user cd'd to (`811a4df`).
- 400 tests pass (`dotnet test tests/Ochre.Tests/Ochre.Tests.csproj`).

## Slice 2 scope (from the plan)

1. Port from Perch (`C:\Users\JonHowell\Documents\git\personal\perch`, main = `9275e0b`, paths under `src/Perch.Core/Data/`), with their tests from `tests/Perch.Tests`:
   - `GitRunner.cs` (289 lines): the only place git is started; neutralises repo-defined commands (fsmonitor etc.) via a `Trust` enum, caps concurrent git processes, timeouts. Depends only on `ExecutableResolver` (already in `src/Ochre.Core/Claude/`). Tests: `GitRunnerTests` (12, temp repos).
   - `GitCheckoutScanner.cs` (137, pure BCL): finds a repo root, reads config values and remotes from disk.
   - `GitWorktrees.cs` (107): `GitWorktree`/`GitWorktreeSet`/`GitWorktreeScanner`, reads worktrees from `.git` on disk without running git, handles the `.bare` layout. Tests: `GitWorktreesTests` (5).
   - `WorktreeLayout.cs` (193): path template `{root}/{parent}/{repo}/{name}` plus inference. Depends on `RepoCheckoutResolver.IsPerchWorktree` in `GitHubCheckouts.cs` (138; also holds `GitRemote`). Tests: `WorktreeLayoutTests` (14). Port what's needed; rename Perch-specific bits.
   - Status: Perch's `GitRepoService.cs` is 1,101 lines; port only the status read (`git status --porcelain=v2 --branch --no-optional-locks`) and its models (`GitRepoModels.cs`, 120 lines: `GitRepoStatus`, `GitInProgress`).
   - Not this slice: `SessionWorktree.cs`, `PrWorktree.cs`, anything under `GitGraph/` (slice 6).
   - Commit message must name Perch, the source paths and `9275e0b` (CLAUDE.md, "Copying from Perch"). Code copied keeps its internal visibility; the module project needs `InternalsVisibleTo` like the terminals one.
2. A new module `modules/Ochre.Modules.Git` (id `git`, on by default): each task repo's branch, dirty count and ahead/behind, on the task detail and on Today. Refresh on window focus and after an agent's turn ends, never on a tight timer.
3. A kernel extension point `WorktreeProvider` (ADR 0012 names it; it doesn't exist yet): create, list, remove a checkout for a task. Add it to `IModuleContext` as `AddWorktreeProvider(...)` and keep the providers in the kernel (`ModuleKernel`/a small registry in Core), so slice 3's sprig provider plugs in the same way. The git module provides `git-worktree` (`git worktree add` under a `WorktreeLayout` path, through `GitRunner`).
4. "Start agent" (`ui/src/sessions/StartAgentDialog.tsx`, host `SessionApi` `sessions.start`) and "Open terminal" (`ui/src/modules/terminals/NewTerminalDialog.tsx`) gain "in a new worktree for this task" when a provider is running. The task records it in a new `worktrees` frontmatter list (`TaskDocument` already preserves unknown keys; add parsing and a round-trip fixture test, since task files are a data-loss boundary).
5. With the git module off, repos are plain folders and agents start in them, as now. Re-run the "every module off" check.
6. Write `docs/testing/m4-git-testing.md`, add a "Done" note to the slice in the plan, commit as `M4 slice 2: ...`.

Decide while building (and record the choice in the plan's Done note): the worktree layout root (Perch's default vs a folder beside the repo); how a task's worktree is removed (a confirmed dialog; never delete a branch with unpushed work without saying so).

## How to add a module (as slice 1 did it)

- Project `modules/Ochre.Modules.<Name>/` referencing Core; `InternalsVisibleTo` for `ochre`, `Ochre.BridgeGen`, `Ochre.Tests`. Add `InternalsVisibleTo Include="Ochre.Modules.<Name>"` to `src/Ochre.Core/Ochre.Core.csproj`.
- Add it to `Ochre.slnx`, as a `ProjectReference` in `src/Ochre.Host/Ochre.Host.csproj` (and `tests/Ochre.Tests` if tested), and to `src/Ochre.Host/ModuleCatalog.cs`.
- Bridge messages are named `<id>.*`, records live in namespace `Ochre.Modules.<Name>.Bridge` with the module's own `JsonSerializerContext` (copy `TerminalsJson` in `modules/Ochre.Modules.Terminals/Bridge/TerminalMessages.cs`). Record names must be unique across the whole bridge; BridgeGen refuses clashes.
- Declare `Requests`/`Events` on the module; register handlers in `Start` with `context.AddBridge`; broadcast with `context.Broadcast("<id>.event", ...)`.
- UI: `ui/src/modules/<id>/index.tsx` exporting a `ModuleUi` (a title-bar `view` and/or a `TaskSection`), added to the list in `ui/src/modules/index.ts`. Today has no module hook yet; slice 2 needs one (a per-task contribution on Today's cards), so extend `ModuleUi` in `ui/src/modules/types.ts`.

## Working in this repo (learned the hard way)

- Build and run: from PowerShell, `$env:OCHRE_DEV_DESKTOP='77f02c4b-4477-477b-8f5c-ba61812616b8'; .\run.cmd` (quits the dev host, regenerates bridge types, builds UI and host, starts it on the user's "safety" virtual desktop so no window lands on their screen). With that variable set, the tray reuses one window by design.
- Drive the window with `src\Ochre.Cli\bin\Debug\net10.0\ochre-cli.exe dev eval "<js>"` called **directly from PowerShell**, not `cli.cmd` (cmd mangles `>`). `ExecuteScript` doesn't await promises: post bridge messages with `window.chrome.webview.postMessage(JSON.stringify({id, type, payload}))` and read replies in a second eval via a `message` listener. Use ids above 900000.
- Screenshots: `ochre-cli.exe dev capture "$PWD\captures\<yyyyMMdd-HHmmss>-<reason>.png"` (gitignored; never delete them).
- The running dev host locks `src/Ochre.Host/bin`; build the tests project alone, or quit the host (`run.cmd` does).
- **Never edit code containing backslash escapes through the shell**: Bash-tool heredocs collapse `\\`, Windows `python3` turns `\n` into `\r\n`, sed treats `\u` specially. Use the Edit/Write tools. In C# use `\u001b`, not `\x1b` (`\x` is variable length).
- Don't run `find` on system folders; don't use `certutil`, base64 decoding or `-EncodedCommand` (user's EDR rules).
- Markdown: never break a sentence across lines. Comments explain why, never what code used to do.
- The user prefers dialogs for creates and destructive actions, counts on filters, status colours (`docs/ux-conventions.md`); a larger visual change starts from a design artefact agreed first. Slice 2's UI is small (status lines and a dialog option); slice 6 (git graph) needs an artefact.

## Later slices, for context

- Slice 3 sprig: port `src/Perch.Core/Data/Sprig/*` (`SprigCli`, `SprigStatus`, `SprigStatusFold`, 38 tests). sprig CLI contract: `sprig docs agent`; `checkout -b <branch> --json` makes a slot, `up`/`start --no-wait` runs it, `watch --json` streams status (one long-lived process), `logs <app> -n 200` is plain text, `free --json` gives a slot back. Failures are `{ok:false, code, error}`.
- Slice 5 Jira: ask the user for a Jira site before starting; token in Credential Manager behind a Core `ISecretStore`.
