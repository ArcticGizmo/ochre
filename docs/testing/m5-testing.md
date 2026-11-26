# M5 exit run: attention inbox and Roost grid

The exit criterion from `docs/roadmap.md`, walked on 2026-11-26 in a headless dev host on T-0013 with three real agents (exit-check, inbox-check and a new grid-check). The feature docs behind it are `m5-attention-testing.md`, `m5-inbox-testing.md`, `m5-notifications-testing.md`, `m5-roost-model-testing.md` and `m5-grid-testing.md`.

| Exit step | How it was checked | Result |
|---|---|---|
| Three agents on a task stream side by side in its grid | Grid on Three columns; inbox-check put in region 2 from the empty region's picker; grid-check started on the task, which filled region 3 by itself; long replies asked of exit-check and grid-check while inbox-check asked for a Write | Pass. All three streamed at once. Main thread over the run: no long tasks (over 50 ms) and a worst 100 ms timer delay of 14 ms |
| A permission is answered in a pane | Deny on the Allow Write card in inbox-check's pane | Pass. The agent carried on and replied that it was denied |
| Another window's inbox shows a question and a finished turn grouped by task | `ochre-cli open T-0013 --new`, Inbox in that window; grid-check asked an AskUserQuestion | Pass. One T-0013 group: grid-check's question with its three options, and the finished turns of the other two |
| Answering or opening them clears them everywhere | The question answered in window 1's grid pane; a finished turn dismissed in window 2 | Pass. The question left window 2 at once (grid-check's reply then showed as finished); the dismissed turn left window 1's `attention.list` |
| A permission prompt pops a notification that can allow it | A Write into the gitignored `captures/` folder; `ochre-cli dev notify <session> allow` | Pass. The recorded notification had Allow, Deny and Open; Allow let the Write through |
| Answering inside Ochre takes the notification away | Deny in the pane while the prompt's notification was recorded | Pass. The prompt's notification was replaced by the agent's finished one |
| With the `roost` and `notifications` modules off, the inbox still works | Both set to false in the dev `settings.json`, host restarted; a question asked and answered in the inbox | Pass. No Grid tab, `roost.get` refused as an unknown message, no notifications recorded, and the question showed and was answered in the inbox |
| An outside `claude` linked to the task shows read-only in the grid and in the inbox while it waits | | **For the hand run** (needs a `claude` you run in a terminal, linked to the task, waiting on a prompt) |

## Still for the hand run

A headless window never has focus and records notifications instead of showing them, so these need you at the keyboard:

- **The outside `claude` row above**: run `claude` in a terminal in a task's repo, link it from the task's suggestion bar, make it wait on a permission prompt, and check it shows read-only in the grid (no composer) and in the inbox.
- **Opening clears**: open a finished turn from the inbox (Open thread) in a focused window and check it leaves the other window's inbox. Seen is only reported from a focused window, which is why this wasn't checkable headless; dismissing was.
- **Real notifications**: the table in `m5-notifications-testing.md`, including clicking one from the notification centre.
- **The grid by hand**: real mouse drags, and the rows `m5-grid-testing.md` lists as not yet checked. Eight panes streaming at once wasn't tried (three were, with no long tasks); try it if you run that many agents.
