# M6 slice 1: file access in the host

The new `editor` module (on by default) lets a window list, read and save the files in a task's folders, see what changed since HEAD, and hear when an open file changes on disk (ADR 0015). Nothing in the UI uses it yet (that's slice 2, the Files view), so these checks go through the bridge in a headless dev host. The automated tests cover the boundaries: `TaskPathsTests` (where a path may lead), `TextFilesTests` (saves never overwrite an unseen change, and keep the BOM and line endings) and `EditorApiTests` (the bridge, against a real repo).

## Setup

1. `$env:OCHRE_DEV_HEADLESS='1'; $env:OCHRE_DEV_DESKTOP='<a spare virtual desktop guid>'; .\run.cmd`
2. Use a task whose repo is this checkout (the dev profile's T-0013), and in one `dev eval` install a listener that keeps replies and `editor.changed` events:

```
ochre-cli.exe dev eval "(function(){ window.__r = {}; window.__ev = []; window.chrome.webview.addEventListener('message', function(e){ var d = JSON.parse(e.data); if (d.id) window.__r[d.id] = d; if (d.event === 'editor.changed') window.__ev.push(d.payload); }); return 'ok'; })()"
```

3. Send a request with `window.chrome.webview.postMessage(JSON.stringify({id: 900001, type: 'editor.list', payload: {taskId: 'T-0013', folder: '<the repo, backslashes doubled>'}}))`, then read `JSON.stringify(window.__r[900001])` in a second eval.

## Checks

| Try | Before | After |
|---|---|---|
| `editor.list` on the task's repo | No such message | Every file git lists (tracked and untracked, `.gitignore` honoured), `git: true` |
| `editor.read` of `README.md` | | Its text, a 64-character `version`, `problem: "none"`, `readOnly: false` |
| `editor.read` of `../x.txt`, `.git/config`, `CON` or a folder the task doesn't record | | Refused, saying why |
| `editor.changes` with uncommitted edits in the repo | | One entry per folder with each changed file, its letter and +/- line counts |
| `editor.head` of a changed file | | Its text at HEAD |
| `editor.head` of an unchanged file | | Refused: "that file has no changes since HEAD" |
| `editor.locate` with the absolute path of a file in the repo | | The folder and the path relative to it; null for a path outside the task's folders |
| Create a scratch file, `editor.watch` it, then change it from PowerShell | | One `editor.changed` event about 200 ms later, with the new version |
| `editor.write` naming the version read before that change | | `saved: false` with the new version; the file keeps the outside change |
| `editor.write` naming the current version | | `saved: true` and the new version; the file has the new text |
| Close the window, change the file again | | No event (the window's watches went with it) |

Checked 2026-11-26 in the headless dev host: every row except the last (closing the window), with a scratch file at the repo root, removed afterwards.
