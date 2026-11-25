# M1 slice 3: task list and detail

What changed: tasks can be created and worked on in the window. The host exposes task messages over the bridge (validated on the host), and the UI's TypeScript types are generated from the C# contract (ADR 0009). Notes render as sanitised markdown. Edits made to a task file outside Ochre show up in the open window.

## Setup

1. `pnpm install` and `pnpm build` in `ui/`, then `dotnet build Ochre.slnx`.
2. Run `src\Ochre.Host\bin\Debug\net10.0-windows\ochre.exe` (dev profile; tasks go to `%APPDATA%\Ochre-dev\tasks`).

## Checks

| Try | Before | After |
|---|---|---|
| Click "New task", fill in title, a markdown description and two repo paths (one per line), Create task | No way to create tasks | The dialog closes; the task opens with the description as its notes and both repos; the file has them all and a single "created" log line |
| Open "New task" and press Esc | | The dialog closes and nothing is created |
| On a task, click Delete | | A dialog names the task and says the file moves to `.trash`; Cancel leaves it alone |
| Confirm the delete | | The task leaves the list, its file is in `tasks\.trash`, and the next new task gets the next id (the deleted one isn't reused) |
| In an editor, add plain text and a bare `- bullet` under `## Plan`, save | Neither showed: the edit looked lost | The text shows above the steps and the bullet shows as a step within a second; the log says "changed on disk"; ticking it in Ochre writes `- [x] ...` with an id |
| Click "active" in the state chips | | The task shows ACTIVE in the list; the log gains "set state active" |
| Type in "Add a step" and press Enter, three times | | Three plan items, each with an id (`{#p1}`...) in the file |
| Hover a step, click "+", type, Enter | | A nested sub-step appears under it, indented in the file |
| Tick a step | | Struck through; the list shows 1/N and the next unticked step as "Next:" |
| Edit notes: `**bold**, [a link](https://example.com), <script>alert(1)</script>`, Save | | Bold and the link render; no script runs and the tag is gone from the rendered notes (it stays in the file as typed) |
| Click the link in the notes | | Opens in your browser, not inside Ochre |
| Middle-click the link | | Opens in a new browser window |
| Add a repo path like `C:\git\some\very\long\path\repo` in a narrow window | | The path shows with the leading folders elided and `repo` visible; the full path is in the tooltip |
| Add a relative path like `repo` | | Error banner: not an absolute path |
| With the task open, edit its `.md` file in an editor (tick a step) and save | | The window updates within a second, no reload |
| Break the file's frontmatter (delete the closing `---`), then tick a step in the UI | | Error banner explaining the file can't be read; the file is untouched. Fix the file and the task works again |
| Rename the task title inline, press Enter | | Title updates in the list and in the file |
| Filter chips: Open / Active / Parked / Backlog / Done | | Lists filter; Open hides Done |
| `dotnet run --project tools/Ochre.BridgeGen -- --check` | | "bridge-types.ts is up to date" |

## Dev aids used to verify this slice

Dev builds accept `cli dev eval "<js>"` (runs in the page, prints the result) and `cli dev capture <png>` (saves what the page shows), sent to the running instance over its per-user pipe. With `OCHRE_DEV_DESKTOP=<virtual desktop guid>` set on the first launch, the window opens unfocused on that desktop.
