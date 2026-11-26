# Handover: M5 slice 6 (grid view)

Written 2026-11-26 at the end of a long session, for the next one to pick up cold. Branch `rename-to-ochre`; nothing is pushed. The user wants one commit per finished slice and never a push unless asked.

## Where things stand

M5 is planned in `docs/implementation-plan.md` (M5, seven slices, with an exit criterion) and placed by [ADR 0013](../decisions/0013-attention-inbox-and-roost.md): the inbox is kernel; the grid (`roost`) and `notifications` are modules. Scope agreed with the user: the grid shows one task's agents, failed CI waits for a GitHub module, rich notifications are in. Each done slice has a Done note in the plan with the decisions made while building, and a testing doc.

| Slice | Commit | Testing doc |
|---|---|---|
| Plan, ADR 0013 | `82de198` | |
| 1. Notifications spike | `0d4b0a7` | `docs/spike-notifications-results.md` |
| 2. Attention model (`AttentionBoard`) | `bdae086` | `docs/testing/m5-attention-testing.md` |
| 3. Inbox view (design `docs/design/m5-inbox/`) | `33eed41` | `docs/testing/m5-inbox-testing.md` |
| 4. Notifications module | `5388e51` | `docs/testing/m5-notifications-testing.md` |
| 5. Roost model (`roost` module, no UI) | `a3d7d12` | `docs/testing/m5-roost-model-testing.md` |
| 6. Grid view: design agreed, not built | this commit | |

636 tests pass (`dotnet test tests/Ochre.Tests/Ochre.Tests.csproj`).

## Next: slice 6, the grid view

The design is agreed: `docs/design/m5-roost/` (README lists the artboards and the three choices the user accepted), published at https://claude.ai/artifact/EbEaZCrYVKL9YAEFfpbz3E. Read the plan's slice 6 too. What the host already gives you (`modules/Ochre.Modules.Roost/`, types in `ui/src/generated/bridge-types.ts`):

- `roost.get {taskId, aspect}` returns the whole grid (`RoostGridDto`): `units` (12), regions in reading order with `number`, unit position and span, and `sessionId`; `zoomed`; `unplaced` (the task's sessions not in the grid, start order); `preset` (the preset the layout matches, or null); `presets` (key, name, regions, for the Layout menu thumbnails). `aspect` (stage width / height) only shapes a grid the task doesn't have yet.
- `roost.applyPreset {taskId, preset}` (a preset key), `roost.assign {taskId, regionId, sessionId}` (swaps if the session was in another region; the occupant leaves if it came from outside), `roost.unassign`, `roost.zoom {taskId, regionId|null}` (toggles). Each returns the whole grid and broadcasts `roost.changed {taskId}`; refetch on that, on `sessions.changed`, and when the task's session list changes (a new agent fills an empty region on the next `roost.get`).

What to build, from the plan and the design:

- **A module view** in `ui/src/modules/roost/` (see `ui/src/modules/git/` for a view with a per-window parameter: the task id), plus a **Grid** button beside a task's agents on its page (the git module's `TaskSection`/folder contributions show how a module adds to the task page; it may need a new contribution point for the agents header). The view is a CSS grid, `grid-template-columns/rows: repeat(12, minmax(0, 1fr))`, each region at `grid-row: row+1 / span rowSpan`.
- **Panes**: a header (number, status dot and word from `tasks/present.tsx`, name via `sessionName`, model and context, zoom and a menu with Open thread and Remove from grid) and a compact `SessionView`. `SessionView` today is a whole page (back button, header, meta, thread, composer); give it a `compact` mode or split out the thread and composer. Every pane filters the one `sessions.update` stream, so N panes cost no extra subscriptions. Observed sessions: read-only note, no composer. Dormant: the composer says "Resume and send".
- **Placement**: drag a pane's header onto another pane (HTML5 drag and drop is enough) to `roost.assign` into that region; click an empty region for a picker (unplaced first, then placed ones, "moves here"); the "Not in the grid" chips in the toolbar; Layout menu from `presets`.
- **Keys**: Alt+1–8 focus a region's composer, Ctrl+Shift+Z zoom the focused pane, Esc restore, and Ctrl+. inside the grid goes to the next pane needing you (App.tsx owns Ctrl+. today via `nextNeedingYou`; let the grid view take it when it's showing and a pane needs you). Leave a focused terminal alone, as Ctrl+K does.
- **Attention**: each pane on screen counts as viewed. Report every visible pane's session in `attention.viewing` (App.tsx builds it from `viewedSession`; the grid needs to pass its list up, or report it itself) and call `useReportSeen` per pane, so finished turns in view clear and notifications stay quiet. A zoomed grid has only the zoomed pane in view.
- **Performance check** from the plan: eight streaming panes stay responsive; if not, panes zoomed away drop their thread and show the last lines.
- Then a testing doc (`docs/testing/m5-grid-testing.md`) and a Done note in the plan, and commit.

After slice 6: **slice 7, the exit run** (the exit criterion is at the top of the plan's M5), then `docs/testing/m5-testing.md` and mark M5 done in the roadmap and CLAUDE.md.

## Still for the user to check by hand

- **Real notifications**: everything in the table of `docs/testing/m5-notifications-testing.md`. Headless runs record them, so nothing has been seen on screen yet. Includes clicking from the notification centre (never checked).
- **Inbox rows not walked**: plan approval with feedback, Ctrl+. with something waiting, the tray tooltip, two windows (`docs/testing/m5-inbox-testing.md`, last paragraph).
- From M4, still open: the real Ctrl+Alt+Space, the git graph on a repo with merges and stashes, window placement on a changed monitor.

## Testing without disturbing the user

Unchanged from `docs/handover/m5-start.md` (read its section), plus:

- **Notifications in dev**: a headless host records them instead of showing them. `ochre-cli dev notifications` lists what would be showing; `ochre-cli dev notify <tag> <action> [key=value]` presses a button (`deny`, `answer choice=1`, `send text=...`, `open`). The tag is the session id. `OCHRE_DEV_NOTIFY=1` shows real ones (they appear on the user's screen; ask first).
- **The host's output folder moved** to `src/Ochre.Host/bin/Debug/net10.0-windows10.0.19041.0/` (run.cmd and cli.cmd know). The old `net10.0-windows` folder is stale; ignore it.
- **Bridge from `dev eval`**: `window.chrome.webview.postMessage` takes a JSON **string**; posting an object is silently ignored. Install a listener that keeps replies with ids above 900000 in one eval, read them in the next.
- **Real agents for UI checks**: test task T-0013 has this checkout as its repo and two agents, one with role `inbox-check` (dormant; sending resumes it). Your Claude settings pre-allow `git status` and `uname`, so ask for a Write to get a permission prompt, and Deny it. AskUserQuestion with three options gives a question. Stop the agent (`sessions.stop`) when done.

## Working in this repo (learned this session)

- **Adding a module**: a project under `modules/` with `InternalsVisibleTo` for `ochre`, `Ochre.BridgeGen`, `Ochre.Tests`; add it to Core's `InternalsVisibleTo` list in `src/Ochre.Core/Ochre.Core.csproj`, the host's project references, `src/Ochre.Host/ModuleCatalog.cs`, `Ochre.slnx`, and the tests project if it has tests. Its bridge records must live in a `<Module>.Bridge` namespace for BridgeGen. Run BridgeGen after.
- **Modules reach the attention inbox** through `IModuleContext.Attention` (board, views, actions) and can bring a task's window forward with `ShowTask`.
- **C#**: a local function can't be overloaded (CS0128); a raw interpolated string with one `$` can't hold `{{` or `}}}}` (use `$$` or concatenate); `Assert.Equal` on `string?[]` fails to compile under nullable (map nulls to a marker).
- Everything in `docs/handover/m5-start.md`'s "Working in this repo" still holds (Edit/Write for code with escapes, commit messages from a file, BridgeGen after bridge changes, the untrusted renderer, no eslint, markdown sentences on one line).
