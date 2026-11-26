# Handover: M4 slice 4 (multiple windows)

Written 2026-11-26 at the end of a long session, for the next one to pick up cold. Branch `rename-to-ochre`, last commit `b1ec4e2` (M4 slice 3). Nothing is pushed; the user wants one commit per finished slice, never a push.

## Where things stand

- M0 to M3 are done. M4 is planned in `docs/implementation-plan.md` ("M4: module host and first modules"); each finished slice has a Done note there recording what was decided. Read the slice 4 section, the roadmap's "Multiple windows" requirement (`docs/roadmap.md`), [ADR 0008](../decisions/0008-tray-host-process.md) (the host owns state; windows are views) and [ADR 0012](../decisions/0012-module-contracts.md) first.
- **Slice 1 (module host)**, `2104a39`: `IModule`/`IModuleContext`/`ModuleHost` in `src/Ochre.Core/Modules/`; modules under `modules/`; UI registry in `ui/src/modules/`.
- **Slice 2 (git module and worktrees)**, `cdfe141`: git status beside each task folder; the `IWorktreeProvider` extension point with kernel `worktrees.*` bridge messages (`src/Ochre.Host/Bridge/WorktreeApi.cs`); the task file's `worktrees` list; the start dialogs' "A new worktree for this task..." (`ui/src/worktrees/`). Also added the dev-only headless mode (below).
- **Slice 3 (sprig module)**, `b1ec4e2`: a `sprig` worktree provider, one `sprig watch --json` feeding app state to the task and Today, Start/Stop/Restart and logs. Off by default. The git primitives moved into Core (`src/Ochre.Core/Git/`).
- 502 tests pass (`dotnet test tests/Ochre.Tests/Ochre.Tests.csproj`). `ControlledSessionsTests.Changed_FiresOnRealTransitions_NotOnNoOps` failed once in a full run and passed alone: it shares static state with tests running in parallel. Worth a look if it recurs; not caused by slices 2 or 3.

## Slice 4 scope (from the plan and roadmap)

The last part of M4's exit criterion: **two tasks open in two windows on two virtual desktops come back on those desktops after quitting and relaunching.**

1. Open a task in its own window: from the task list (context menu), a button on the task, and `ochre open T-0042` (the CLI's `open` today takes no argument: `src/Ochre.Cli/Program.cs`, sent over the instance pipe as `"open"` to `src/Ochre.Host/Program.cs`). A new window opens on the **current** virtual desktop.
2. Each window remembers its task (or none), size, position and virtual desktop (`GetWindowDesktopId`) in `%LOCALAPPDATA%\Ochre\windows.json` (the dev profile has its own local root; use `OchrePaths`). On relaunch each window reopens on its desktop (`MoveWindowToDesktop`), falling back to the current desktop if that one is gone.
3. Opening a task already shown in a window on another desktop asks: bring that window here, or open another view here. It never switches desktops by itself.
4. Position is per monitor: port Perch's `PlacementMath` with its tests, so a window whose monitor is gone comes back on the primary.
5. Closing a window closes only that view (ADR 0008); quitting from the tray saves every open window's state first.
6. Write `docs/testing/m4-windows-testing.md`, add a Done note to the slice in the plan, commit as `M4 slice 4: ...`.

Decide while building (and record in the Done note): how a window tells the host which task it shows (a bridge message from the page when its selection changes, or the host passing the task in the page URL when it opens the window, or both); whether a window showing a thread rather than the task restores to the thread; what happens to `OCHRE_HIDE_ON_CLOSE` (hidden windows are reused today).

## What already exists for windows

- `src/Ochre.Platform.Windows/Shell/WindowsShell.cs`: the tray, the WinForms message loop and the window list (`_windows`). `OpenWindow()` brings forward a window on the current desktop or opens a new one; earlier this session (`14b819e`) it learned to open on the current desktop rather than switch desktops.
- `src/Ochre.Platform.Windows/Shell/VirtualDesktop.cs`: `IVirtualDesktopManager` through COM source generation, with `TryMove` and `IsOnCurrentDesktop`. `GetWindowDesktopId` is declared on the interface but has no wrapper yet.
- `src/Ochre.Platform.Windows/Shell/MainWindow.cs`: one WebView2 form. Fixed 1400x900 centred today; nothing saves its size or position.
- `src/Ochre.Core/Hosting/Shell.cs`: `IShell` (Core's view of the shell). Anything the host needs from windows goes behind it; keep OS calls in `Ochre.Platform.Windows`.
- The page keeps per-window state (view, selected task, open thread) in React state in `ui/src/App.tsx`; host events are broadcast to every window (`WindowHost.Broadcast`). Keep it that way (CLAUDE.md: "Never assume one window").
- Perch: `C:\Users\JonHowell\Documents\git\personal\perch` (main `9275e0b`). `src/Perch.Core/Data/PlacementMath.cs` (a public static class) and `tests/Perch.Tests/PlacementMathTests.cs`. Its callers (`OverlayPlacement.cs`, `App.axaml.cs`) are Avalonia and don't port; take the math only. The commit message must name Perch, the paths and `9275e0b`.

## Testing it without disturbing the user (read this first)

- **Always start the dev host headless**: `$env:OCHRE_DEV_HEADLESS='1'; $env:OCHRE_DEV_DESKTOP='77f02c4b-4477-477b-8f5c-ba61812616b8'; .\run.cmd`. The user asked for this explicitly after windows kept appearing on their screen. Headless (Debug builds only) puts windows at (-32000, -32000) with no taskbar button and no focus; `dev eval` and `dev capture` still work.
- **This slice collides with both dev settings.** With either set, `ShellOptions.Unattended` is true and `OpenWindow` deliberately reuses a single window, and every window is pushed to the one safety desktop. To test several windows on several desktops you'll have to change that rule (for example: unattended still opens a new window for a task, and `windows.json` desktops are honoured), and you need a **second** virtual desktop to restore onto. Ask the user for a second desktop GUID, or ask how they want the desktop restore checked; don't create or switch desktops yourself, and don't move windows onto the user's current desktop. A restored position at -32000 is meaningless, so the headless offset must not be saved into `windows.json`.
- Drive the page with `src\Ochre.Cli\bin\Debug\net10.0\ochre-cli.exe dev eval "<js>"` called directly from PowerShell (not `cli.cmd`; cmd mangles `>`). `ExecuteScript` doesn't await promises: install a listener on `window.chrome.webview` (not `window`) in one eval, post `{id, type, payload}` with ids above 900000, and read the replies in a second eval. `dev eval` and `dev capture` go to one window; with several open you'll need a way to pick which (extend `IShellDevTools`).
- Screenshots: `ochre-cli.exe dev capture "$PWD\captures\<yyyyMMdd-HHmmss>-<reason>.png"` (gitignored; never delete them).
- The running dev host locks `src/Ochre.Host/bin`: build the tests project alone, or quit the host (`run.cmd` does). `ochre-cli.exe quit` stops it; `run.cmd -NoBuild` restarts without building.
- Dev test data: tasks T-0011 and T-0012 in the dev profile point at repos in the previous session's scratchpad, which will be gone; delete those tasks or ignore them. The dev profile has the sprig module switched on.

## Working in this repo (learned the hard way)

- **Never edit code containing backslash escapes through the shell**: heredocs collapse `\\`, Windows `python3` turns `\n` into `\r\n`, sed treats `\u` specially. Use the Edit/Write tools. In C# use `\u001b`, not `\x1b`.
- **Commit messages from PowerShell**: `git commit -F -` with a here-string passes it as an argument, not stdin. Write the message to a file in the scratchpad and use `git commit -F <file>`.
- After changing a bridge record or a contract, run `dotnet run --project tools/Ochre.BridgeGen` and commit `ui/src/generated/bridge-types.ts`; `-- --check` verifies it.
- Don't run `find` on system folders; don't use `certutil`, base64 decoding or `-EncodedCommand` (the user's EDR rules).
- Markdown: never break a sentence across lines. Comments explain why, never what code used to do.
- UI: dialogs for creates and destructive actions, counts on filters, status colours (`docs/ux-conventions.md`). The "bring that window here or open another here" question is a dialog. A larger visual change starts from a design artefact agreed first; this slice's UI is small (a menu item, a button, one dialog).
- Testing stance: automated tests for data-loss and security boundaries and for code ported from Perch (bring `PlacementMathTests`); `windows.json` reading should tolerate a missing, partial or corrupt file without throwing. Everything else is checked by hand through the testing doc.

## Later slices, for context

- Slice 5 Jira: ask the user for a Jira site before starting; the token goes in Credential Manager behind a Core `ISecretStore`.
- Slice 6 git graph: port the graph model and loader with `GitGraphTests`; starts from a design artefact. The git primitives it needs are now in `src/Ochre.Core/Git/`.
- Slice 7: walk the exit criterion by hand, write `docs/testing/m4-testing.md`.
