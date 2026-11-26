# Handover: start of M5 (attention inbox and Roost grid)

Written 2026-11-26 at the end of a long session, for the next one to pick up cold. Branch `rename-to-ochre`, last commit `b85b33d`, working tree clean. Nothing is pushed; the user wants one commit per finished slice and never a push unless asked.

## Where things stand

- **M0 to M4 are done.** M4's exit criterion was walked on 2026-11-26 (`docs/testing/m4-testing.md`). The Jira module moved out of M4 to the roadmap's Later list at the user's request; its plan stays in `docs/implementation-plan.md` (M4 slice 6) and it needs a Jira site to test against.
- What M4 added, each with a Done note in the plan and a testing doc in `docs/testing/`:
  - Module host and module settings (`m4-modules-testing.md`).
  - Git module: status beside task folders and worktrees through the `IWorktreeProvider` extension point (`m4-git-testing.md`).
  - Sprig module: sprig slots as worktrees, app status and logs (`m4-sprig-testing.md`).
  - Multiple windows: task windows, `windows.json`, windows reopening on their virtual desktop and monitor (`m4-windows-testing.md`).
  - Git graph: a read-only **Git** view, ported from Perch, with a **Graph** link beside each task folder (`m4-graph-testing.md`; design in `docs/design/m4-graph/`).
  - Jump to task: Ctrl+Alt+Space from any app (in Settings, `jumpHotkey` in `settings.json`), Ctrl+K inside a window (`m4-jump-testing.md`).
- 586 tests pass (`dotnet test tests/Ochre.Tests/Ochre.Tests.csproj`). The flaky `ControlledSessionsTests` from the last handover is fixed (`54baefc`).

## Next: plan M5

The plan says to re-plan each milestone in detail when it starts (`docs/implementation-plan.md`, "M5 onwards"), so M5 starts with a plan, not code. Write it into the plan as slices with an exit criterion, the way M4 was planned ("Planned 2026-11-26 from a survey of Perch ...").

Roadmap scope (`docs/roadmap.md`, M5; `docs/vision.md`):

- **Attention inbox**: one queue of everything that needs the user (permission prompts, questions, finished turns, failed CI), grouped by task. Antigravity's inbox is the reference in `docs/landscape.md`.
- **Roost-style grid** of a task's sessions, interactive for controlled sessions and read-only for observed ones, "or across tasks" (vision).

Before writing the plan:

1. Survey Perch's Roost (Perch main is still `9275e0b`, at `C:\Users\JonHowell\Documents\git\personal\perch`). The UI-free model is in `src/Perch.Core/Data/Roost/` (`RoostGridLayout`, `RoostRoster`, `RoostTabSet`, `RoostLayoutLibrary`, `RoostTemplates`, `RoostKeys`, `RoostToken`), probably with tests in `tests/Perch.Tests/`. The Avalonia window (`src/Perch.App/Windows/RoostWindow*.cs`, `Views/Roost*.cs`, `Services/RoostFeed.cs`) won't port but is the behavioural spec (`docs/reuse-from-perch.md`). Decide what ports and what Ochre needs (tabs and saved layouts may be more than M5 wants).
2. See what Ochre already knows that an inbox can draw on: `SessionManager` (`src/Ochre.Core/Sessions/`) knows waiting permission prompts and questions and finished turns for controlled sessions; observed sessions have a status from `sessions/{pid}.json` (ADR 0006); the task list already flags a task whose agent is waiting with "needs you". Failed CI has no source yet; ask whether it's in M5 or waits for a GitHub module.
3. Both are larger visual changes: each starts from a design artefact the user agrees first (`docs/ux-conventions.md`, Process). Earlier canvases are in `docs/design/` (Ochre's own colours, not a design system; publish with the Artifact tool's Design type and keep the local copy under `docs/design/<slice>/`).
4. ADR 0007 lists the Roost grid as a module (`Panel` contribution); the inbox may be kernel (like Today) or a module. Decide and record it.

Ask the user before choosing for them: whether the grid spans tasks in M5, and whether CI belongs in the inbox now.

## Still for the user to check by hand

Nothing blocks M5, but these weren't exercised:

- **The real Ctrl+Alt+Space.** Headless dev runs don't register it (by design). Steps in `m4-jump-testing.md`: quit the installed Ochre, `.\run.cmd` without the headless variables.
- **Git graph**: a merge, a stash row and "Load 2,000 more" in a real repo (this repo's history is linear, 48 commits). Unit tests cover the layout and paging.
- **Window placement** on a monitor that's gone or rescaled (unit tests only).

## Testing without disturbing the user (read this first)

- **Always start the dev host headless**: `$env:OCHRE_DEV_HEADLESS='1'; $env:OCHRE_DEV_DESKTOP='77f02c4b-4477-477b-8f5c-ba61812616b8'; .\run.cmd`. Windows open at (-32000, -32000), unfocused, without a taskbar button. In that mode the global hotkey isn't registered; use `ochre-cli dev jump`.
- Drive it with `src\Ochre.Cli\bin\Debug\net10.0\ochre-cli.exe` called from PowerShell (not `cli.cmd`, which mangles `>`):
  - `dev windows` lists windows (id, task, desktop, bounds);
  - `dev eval --window w2 "<js>"` and `dev capture --window w2 captures\<yyyyMMdd-HHmmss>-<reason>.png` pick a window (default: the last used);
  - `dev move-window <id> <desktop-guid>`, `dev jump`, `dev session-ending`;
  - `open T-0042 [--bring|--new]` opens a task in its own window.
- `ExecuteScript` doesn't await promises: in one eval, add a listener on `window.chrome.webview` that stores replies with ids above 900000 and post `{id, type, payload}`; read `window.__r[id]` in a second eval. A page reload (or a new window) loses the listener.
- **The host serves the UI from its own build output**, so `pnpm build` alone doesn't change what a running host shows: run `.\run.cmd` (it quits the dev host, rebuilds everything and starts it). A dev host left running locks `src/Ochre.Host/bin` and breaks other builds (BridgeGen included); `ochre-cli.exe quit` it first.
- `ochre-cli.exe close` closes every window, which empties the dev `windows.json`; do that before quitting so the next start doesn't reopen test windows.
- Captures go under `captures/` (gitignored); never delete them.
- Dev profile data: `%APPDATA%\Ochre-dev` (tasks, `settings.json`) and `%LOCALAPPDATA%\Ochre-dev` (`windows.json`, logs, `sessions.json`). Modules there: git, sprig and terminals on, shutdown-park off. Test task T-0013 ("M4 exit: kernel with every module off") has this checkout as its repo and a dormant agent; T-0011 and T-0012 point at long-gone scratch repos.
- **Sprig only knows the user's work repos** (InFlight.NextGen and others). Making a slot there makes a branch in a work repo: ask first. InFlight.NextGen's `sprig.yaml` exists only on `jon-sprig`, so a slot from its default branch is refused by sprig (Ochre now records the slot anyway and shows sprig's error).

## Working in this repo (learned the hard way)

- Never edit code containing backslash escapes through the shell; use Edit/Write. In C# use `\u001b`, not `\x1b`.
- Commit messages from PowerShell: write the message to a file in the scratchpad and `git commit -F <file>`. Copying from Perch: name Perch, the paths and `9275e0b` in the message, and bring the matching tests.
- After changing a bridge record or a contract, `dotnet run --project tools/Ochre.BridgeGen` and commit `ui/src/generated/bridge-types.ts`; `-- --check` verifies it. Bridge records use `RespectRequiredConstructorParameters`, so a new constructor parameter (even nullable) must be sent by every caller, UI included.
- The renderer is untrusted. Anything that ends up on a git or sprig command line is checked on the host against what the host itself listed (task folders, graph tokens, base branches from `for-each-ref`); `GraphApiTests` and `WorktreeApi` show the pattern.
- No eslint in `ui/`; don't add suppression comments. TypeScript strictness comes from `tsc` in `pnpm build`.
- Don't run `find` on system folders; no `certutil`, base64 decoding or `-EncodedCommand` (the user's EDR rules).
- Markdown: never break a sentence across lines. Comments explain why, never what code used to do.
- UI: dialogs for creates and destructive actions, counts on filters, status colours (`docs/ux-conventions.md`).
- Testing stance: automated tests for data-loss and security boundaries, the controlled-session lifecycle (in-memory `FakeClaude`), and code ported from Perch; everything else by hand through a testing doc per change.
