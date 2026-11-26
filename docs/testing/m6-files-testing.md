# M6 slice 2: the Files view

The editor module's **Files** title-bar view (design in `docs/design/m6-editor/`): a task's folders as a tree, files in tabs, Monaco to edit them, Ctrl+S to save, a bar when an agent changes a file you're editing, markdown side by side, and a question before a window with unsaved files closes. The host side is slice 1 (`m6-files-host-testing.md`).

## Setup

`$env:OCHRE_DEV_HEADLESS='1'; $env:OCHRE_DEV_DESKTOP='<a spare virtual desktop guid>'; .\run.cmd` for the headless checks, or `.\run.cmd` alone for the hand checks at the end. Use a task with a repo (the dev profile's T-0007 or T-0013 point at this checkout).

Headless, Monaco takes input through the browser's EditContext, which synthetic typing can't reach; a keybinding can. From `dev eval`, focus `.files-content .monaco-editor .native-edit-context` and dispatch `new KeyboardEvent('keydown', {key: 'Enter', code: 'Enter', keyCode: 13, ctrlKey: true, bubbles: true})` to insert a line (an edit), or `{key: 's', code: 'KeyS', keyCode: 83, ctrlKey: true}` to save.

## Checks

| Try | Before | After |
|---|---|---|
| Click **Files** in the title bar | No such view | The task's folders as roots, the first one open; a task menu in the toolbar |
| On a task's page, **Files** beside a folder | | The Files view with that folder open in the tree |
| Open `README.md` | | Source and preview side by side; Source, Preview and Side by side switch; the preview follows typing |
| Click a relative link in the preview (`docs/roadmap.md`) | | It opens in a new tab; the window doesn't navigate. A web link opens in the browser (middle click: a new window) |
| Edit a file | | A dot on its tab, "Unsaved" in the status line |
| Ctrl+S | | "Saved"; the file on disk has the edit, with its BOM and CRLF line endings kept |
| Edit, then change the file outside (an agent, or PowerShell) | | A bar: changed on disk, with Compare, Reload (discard mine) and Keep mine… |
| Compare | | On disk (read-only) against yours, side by side |
| Keep mine…, Save mine | | A confirm first; then yours is on disk and the bar goes |
| Change the file outside while its tab is clean | | The tab reloads by itself |
| Switch to Today and back | | Every tab and unsaved edit as it was |
| A binary file, or one over 5 MB | | Says so, with Close it |
| A file over 512 KB | | Opens read-only |
| Close a tab with unsaved edits (its dot, or middle click) | | Asks: Cancel, Discard, Save and close |
| **By hand:** close the window (its X) with an unsaved file | Closed at once | Asks: Cancel, Discard and close, Save all and close; Cancel keeps the window |
| **By hand:** tray Quit with an unsaved file in a window | Quit at once | That window comes forward and asks (Quit wording); Cancel keeps Ochre running |
| `ochre-cli quit` with an unsaved file | | Quits without asking (the CLI, updates and Windows shutting down never wait) |
| Settings, turn the Editor module off, restart | | No Files tab, no Files links; everything else as before |

Checked 2026-11-26 in the headless dev host, with a scratch file at the repo root (removed afterwards): the Files tab, README.md side by side and its relative link, an edit, Ctrl+S (BOM and CRLF kept on disk), the changed-on-disk bar, Compare, Keep mine with its confirm, and a clean tab reloading. Not yet checked: the task page's Files link, switching views with unsaved edits, the binary, large and read-only cases, closing a dirty tab, and the module off. Closing a window and the tray's Quit need a person (a user close can't be sent headless).
