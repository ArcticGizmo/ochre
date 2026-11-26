# 0015: The editor is a module; the host reads and writes only inside a task's folders

- Status: accepted
- Date: 2026-11-26

## Context

M6 adds a file editor (vision: "good, not an IDE": syntax highlighting, find, diff against HEAD, markdown preview). Scope agreed with the user on 2026-11-26: files are edited and saved, not only viewed; and the editor is reached from a file tree of the task's folders, a task-wide list of changed files, the git graph's commits, file paths in agents' tool cards, and a Ctrl+P file picker.

Until now the renderer could read files only indirectly (a sprig log the host names, a git graph's commits) and could write none. An editor gives it both, so the bridge's file calls become a security boundary (threat 5: an XSS reaches whatever the bridge allows) and a data-loss boundary (a save that silently overwrites an agent's edit, or a file's encoding or line endings changed by a save).

Monaco is the editor (architecture.md, ADR 0002), lazy-loaded so it stays off the window-open path. Both the editor and the git graph (a commit's diffs) need it.

## Decision

- **The editor is the `editor` module**, on by default (ADR 0007). It owns the file bridge (`editor.*`), a **Files** title-bar view whose parameter is a task, the task-wide Changes list, the Ctrl+P picker, and opening a path from a thread.
- **Monaco is shared UI, not the module's.** A lazy-loaded code view and diff view live in `ui/src/editor/`, like `markdown.ts`, so the git module's commit diffs use them without depending on the editor module. With the editor module off, the Git view still shows a commit's diffs (read-only); paths in threads are plain text; there is no Files view.
- **Where a file can be read or written**: only inside a folder the task records (its repos and worktrees). The page names a task, one of its folders and a path relative to it; the host rejects rooted paths, `..` that leaves the folder, device names and alternate data streams, and any path through a link (symlink or junction) inside the folder, rather than resolving where the link leads. Nothing inside a `.git` folder is read or written (a hook written there would run on the next git command).
- **Reads are capped**: text up to 5 MB; a file with a NUL in its first 8 KB is binary and isn't opened; text that isn't UTF-8 (with or without a BOM) opens read-only. A file over 512 KB opens read-only too, since a bridge message holds at most a million characters.
- **A save names the version it edited** (a SHA-256 of the bytes it read). The host writes only if the file on disk still has that version, keeps its BOM, writes atomically (temp file, then replace), and otherwise answers with a conflict the page shows; the user chooses. Two windows editing one file follow the same rule.
- **Content at a commit** (HEAD for the Changes list, a commit and its parent for the graph) is read with `git show <rev>:<path>` only for a path git itself listed for that folder or commit, so nothing the page invents reaches git's command line.
- **No language servers, no file create, rename or delete** in M6 (vision, non-goals).

## Consequences

- The file calls get tests: path containment (threat 5) and save conflicts, BOM and line endings (data loss).
- A window holding unsaved edits has to be asked before it closes, so the shell gains a "may this page close?" question.
- An agent editing an open file is seen through a file watcher on the folders that some window has files open in; a clean buffer reloads, a dirty one asks.
- Monaco adds several megabytes to the UI bundle; it loads on first use only.
