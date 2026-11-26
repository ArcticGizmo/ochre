# Design: editor (M6 slices 2 to 5)

Agreed 2026-11-26, before the UI was changed (`docs/ux-conventions.md`, Process), with its three open choices as drawn (below). Published as a private design canvas at https://claude.ai/artifact/12xqUham9C81jtW18Cs81U; this folder is its local copy. Ochre's own colours, as in `docs/design/m5-roost/`.

Artboards in `project/`:

- `Main.dc.html`: the **Files** title-bar view on a task. The sidebar has Files and Changes (5) tabs; the tree's roots are the task's folders (repos, then worktrees). Editor tabs with an unsaved dot, a breadcrumb with "Compare with HEAD", Monaco's code area, and a status line (Unsaved, Ctrl+S, line and column, encoding with BOM, line endings, language).
- `Changes.dc.html`: the Changes tab, grouped by folder (branch and count), each file with its letter, name (folder elided after it) and +/- counts; the selected file's diff against HEAD side by side, HEAD read-only and the working tree editable, with change navigation and an Inline toggle.
- `OnDisk.dc.html`: a tab with unsaved edits whose file an agent changed. A bar names the file and the agent that probably did it, with Compare (drawn open: disk against yours), Reload (discard mine) and Keep mine (confirmed). A clean tab reloads without asking.
- `Close.dc.html`: closing a window with unsaved files lists them, with Cancel, Discard and close, and Save all and close.
- `Markdown.dc.html`: a README with Source, Preview and Side by side; links to files in the folder open in the editor, web links in the browser.
- `GitDiff.dc.html`: the Git view with a commit's files in its details pane and one opened as a read-only diff under the graph.
- `Thread.dc.html`: file paths in an agent's Read, Edit and Write cards as links (Edit at its line, with a Diff link); a path outside the task's folders stays plain.
- `Picker.dc.html`: Ctrl+P over the task's files, name matches first, with each file's folder.
- `Task.dc.html`: **Files** and **Changes (N)** beside each folder on the task page, next to Graph.

They render only inside the canvas, which supplies `support.js`. Sample tasks, agents, code and folders are made up.

Open choices, as drawn: Files is its own title-bar view holding one task per window (like Git and Grid); a commit's diff opens under the graph rather than replacing it; the changed-on-disk bar names the agent whose last Edit or Write touched that path (a guess, worded "probably").
