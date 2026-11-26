# Handover: M6 slice 3 (Changes and diffs)

Written 2026-11-26 at the end of a session, for the next one to pick up cold. Branch `rename-to-ochre`, last code commit `f363565`, working tree clean. Nothing is pushed; the user wants one commit per finished slice and never a push unless asked.

## Where things stand

- **M0 to M5 are done** (M5's outside-`claude` step and real notifications wait for the user's hand run). Distribution is built (ADR 0014).
- **M6 (editor) is under way.** Plan: `docs/implementation-plan.md`, "M6: editor" (six slices, exit criterion at the top). Placement and file-access rules: [ADR 0015](../decisions/0015-editor-module-and-file-access.md). Scope agreed with the user: edit and save; entry points are a file tree, a task-wide Changes list, git graph commit diffs, paths in agents' tool cards, and a Ctrl+P picker.
- **Design agreed** (all of slices 2 to 5, with three choices as drawn): `docs/design/m6-editor/` (README lists the artboards), published at https://claude.ai/artifact/12xqUham9C81jtW18Cs81U. Build slices 3 to 5 from it: `Changes.dc.html`, `GitDiff.dc.html`, `Thread.dc.html`, `Picker.dc.html`, `Task.dc.html`.
- **Slice 1** (`8aa898c`): the `editor` module, host side. Bridge `editor.list/read/write/head/changes/locate/watch` and event `editor.changed`; `TaskPaths` (containment), `TextFiles` (versioned save, BOM kept, never creates), `GitFolderChanges` (status `-z` + numstat, `ReadAt` via `cat-file`), `ProjectFileScan` (from Perch). Tests: `TaskPathsTests`, `TextFilesTests`, `EditorApiTests`. Testing doc `docs/testing/m6-files-host-testing.md`.
- **Slice 2** (`f363565`): the Files view. Testing doc `docs/testing/m6-files-testing.md` lists what was checked headless and what wasn't. 683 tests pass.

## Next: slice 3, Changes and diffs

From the plan and `Changes.dc.html`:

1. A **Changes (N)** tab in the Files view's sidebar beside Files (`ui/src/modules/editor/FilesView.tsx`, the `files-side-tabs` row already exists with one tab). Data: `editor.changes {taskId}` (already built and tested), grouped by folder; each file with its letter and +/- counts, file name first and its directory muted after it. Refresh on window focus, on `git.changed` (if the git module runs) and on `editor.changed`.
2. A changed file opens as a **diff against HEAD**: left from `editor.head` (refuses a path status didn't list; returns "" for untracked or added), right the tab's own Monaco model, so edits there save like any tab. Use `DiffView` in `ui/src/editor/CodeView.tsx` (takes `original` text and a `modified` model or text). Side by side, with an Inline toggle; `useInlineViewWhenSpaceIsLimited` is already on. A deleted file: its HEAD text, read-only. Binary and oversized: say so.
3. Feed the letters into the tree: `FileTree` already takes a `letters` map (folder lower-cased, then path to letter); nothing passes it yet.
4. On the task page, a **Changes (N)** link beside each folder that has changes (the `FilesLink` FolderStatus in `ui/src/modules/editor/index.tsx`), opening the view on the Changes tab. A clean folder shows Files only.
5. Breadcrumb "Compare with HEAD" on a changed file's tab (in `Main.dc.html`).
6. Ship `docs/testing/m6-changes-testing.md`, a Done note in the plan, commit.

Then slice 4 (graph diffs: `graph.files`, `graph.diff` in `modules/Ochre.Modules.Git/GraphApi.cs` under the token pattern; the UI uses the shared `DiffView`, so it works with the editor module off), slice 5 (tool-card path links via `editor.locate` and a new `ctx.openFile`; Ctrl+P), slice 6 (exit run).

## How the editor UI is put together

- `ui/src/editor/` is **shared, not the module's**: `monaco.ts` (slim Monaco entry, `ochre` theme, `languageFor`), `loadMonaco.ts` (lazy chunk, `useMonaco()`), `CodeView.tsx` (`CodeView`, `DiffView`). The git graph will use these too.
- `ui/src/modules/editor/store.ts` holds every tab **outside React** (Monaco models carry unsaved edits), plus the tree's folder lists and expanded state. Components re-render through `useSyncExternalStore(subscribe, storeVersion)` and read the mutable tab objects directly.
- The view's parameter is JSON `{task, folder?, path?, line?, at?}` (`filesParam`); a new parameter string is acted on once (`handled` ref), and `at` makes repeats distinct.
- Unsaved edits on close: `ui/src/windows/unsaved.ts` (registry), `CloseGuard.tsx` (dialog); host side `window.setUnsaved`, `window.close`, event `window.closeRequested`; shell in `MainWindow.OnFormClosing` and `WindowsShell.QuitAsking`.
- The Files view sits inside an `ErrorBoundary` (`ui/src/ErrorBoundary.tsx`). One crash was found while building: React disposes a component's editor before running its model effect's cleanup, so a cleanup must check the editor is still current (`ed.current === e`).

## Testing without disturbing the user (read this first)

- **Set the headless variables in the same command as every `run.cmd`**: `$env:OCHRE_DEV_HEADLESS='1'; $env:OCHRE_DEV_DESKTOP='77f02c4b-4477-477b-8f5c-ba61812616b8'; .\run.cmd`. PowerShell tool calls don't keep `$env:` between them; a bare `.\run.cmd` to rebuild put a visible window on the user's screen this session.
- Don't pipe `run.cmd` into `Select-Object -Last N` inside a long chain: the output buffers until the end and a stuck step can't be seen. Run it alone, then the checks one per call.
- Drive it with `src\Ochre.Cli\bin\Debug\net10.0\ochre-cli.exe dev eval --window <id> "<js>"`, `dev capture --window <id> captures\<yyyyMMdd-HHmmss>-<reason>.png`, `dev windows`. Before quitting, `ochre-cli close` empties the dev `windows.json` so stale windows don't reopen.
- **Monaco takes input through EditContext**, which synthetic typing and paste don't reliably reach. Keybindings work: focus `.files-content .monaco-editor .native-edit-context` and dispatch `new KeyboardEvent('keydown', {key: 'Enter', code: 'Enter', keyCode: 13, ctrlKey: true, bubbles: true, cancelable: true})` to insert a line, `{key: 's', code: 'KeyS', keyCode: 83, ctrlKey: true, ...}` to save. Store updates render a moment later, so read state in a following eval.
- Scratch files at the repo root (e.g. `editor-test.txt`) work for watch and save checks; delete them afterwards. Dev tasks T-0007 and T-0013 have this checkout as their repo.
- A user close of a window can't be sent headless, so the close and Quit questions are the user's hand checks.

## Still for the user to check by hand

- Closing a window, and the tray's Quit, with an unsaved file (`m6-files-testing.md`).
- From M5: the outside `claude` step, real notifications, opening from a focused window (`m5-testing.md`).
- From M4: the real Ctrl+Alt+Space, the graph on a repo with merges and stashes, window placement on a changed monitor.

## Working in this repo (learned the hard way)

- Commit messages: write to a file in the scratchpad and `git commit -F <file>`; end with the `Co-Authored-By` line. Name Perch, paths and `9275e0b` when copying from Perch.
- After changing a bridge record or contract, `dotnet run --project tools/Ochre.BridgeGen` (run.cmd does it too) and commit `ui/src/generated/bridge-types.ts`. A module's events must start with its id (hence `editor.*`).
- The bridge caps a message at a million characters, so saves are capped at 512 KB (larger files open read-only).
- UI dependencies: exact pins, `pnpm add <pkg>@<version>`; avoid releases only days old. No eslint in `ui/` and no suppression comments; `pnpm build` runs `tsc`.
- No `find` on system folders, no `certutil`, base64 decoding or `-EncodedCommand`. Markdown: never break a sentence across lines. Comments explain why, never what code used to do.
