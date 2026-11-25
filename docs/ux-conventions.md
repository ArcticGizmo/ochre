# UX conventions

How Ochre's UI should behave. Follow these when building any view; propose a change here before diverging.

## Creating and editing

- **No inline creates for things that need more than a name.** Creating a task opens a dialog that takes everything you'd naturally say at once (title, description, repos, and later links), then saves in one go. Typing a title and filling the rest in afterwards is the wrong default.
- **Dialogs mark save boundaries and terminal actions.** Anything destructive or hard to undo (delete, mark done, discard) is confirmed in a modal that names what will happen. Multi-field edits happen in a dialog with explicit Save and Cancel, not as a scatter of fields that each save on blur.
- Small, single-field, reversible edits (ticking a plan item, renaming a title in place) may stay inline.
- Esc cancels a dialog or an inline edit; Enter submits a single-line field.

## Lists and navigation

- Filter groups show their count, e.g. "Active (3)", so you can see where work sits without clicking.
- Lists that can grow have a search box.
- Work that moves through states can be shown as a board (columns per state, e.g. Backlog / Active / Parked / Done) as well as a list.

## Detail views

- The ID sits under the title, not beside it.
- Status is a dropdown, and each status has its own colour, used everywhere it appears (list, board, detail), so state reads at a glance.
- Content that isn't full width has a max width and is centred, rather than leaving a wide empty gutter on the right.

## General

- A file path in a one-line space keeps the file name and elides the leading directory (full path in the tooltip).
- Every browser link also takes a middle click, which opens it in a new browser window.
- Nothing the user writes is silently hidden: if Ochre can't interpret part of a file, it still shows it.

## Process

Larger visual changes start from a design artefact that is agreed before the UI is changed.
