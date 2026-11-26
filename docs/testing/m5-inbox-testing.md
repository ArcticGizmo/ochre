# M5 slice 3: inbox view

A kernel **Inbox** view lists what needs you, grouped by task, and answers it in place. Design: `docs/design/m5-inbox/`. Run the dev host as usual (headless for automated checks, or `.\run.cmd` to try it yourself).

| Try | Before | After |
|---|---|---|
| Look at the title bar | Today, Tasks, Board, then the modules' views | **Inbox** after Board; an amber badge with a count only while something is blocked on you |
| Open Inbox with nothing waiting | No such view | "Nothing needs you" in a dashed box; filters read All 0, Needs you 0, Finished 0 |
| Start an agent on a task with a prompt that finishes without tools | Nothing | A green "finished" item under the task with the reply's last line, a reply box, Open thread and Dismiss; no badge |
| Type in the reply box and press Enter (or Send) | | "Sent", then the item goes; when the agent's turn ends, a new finished item |
| Ask the agent to write a file (default permission mode) | The prompt only in the thread | "wants to use Write" in amber with the path, Allow, Deny and "Allow, switch to acceptEdits"; badge 1; tray tooltip "Ochre: 1 needs you" |
| Click Deny | | "Denied" for a moment, then the item goes; badge clears; the agent is told it was denied |
| Ask the agent to use AskUserQuestion with three options | | The question with its options as a choice list and Answer; Answer stays disabled until one is picked |
| Pick one and Answer | | "Answered: …", then the item goes; the agent's reply names your choice |
| A question with several parts or more than five options | | The first question's text and "Answer in the thread" |
| An agent in plan mode presents a plan | | The plan, cut short with "Show the whole plan"; Approve, a feedback box and Reject. Reject with text sends Claude your text; without, it's a plain rejection |
| Answer a prompt in its thread, or in another window, while the inbox shows it | | The row says "No longer waiting: answered elsewhere, or the agent moved on" for a moment, then goes |
| Click Allow on a prompt that has just gone (two windows, answer in one, click in the other quickly) | | The host refuses it and the row says "that prompt is no longer waiting for an answer"; nothing runs |
| Open a finished agent's thread in the focused window | | Its finished item goes from every window's inbox |
| Leave a thread open in a background window and let the agent finish | | The finished item stays until that window is focused |
| Ctrl+. with items needing you | | Opens the first one's thread; again goes to the next, wrapping |
| Ctrl+. with nothing needing you | | Shows the Inbox |
| An outside `claude` waiting, not linked | | Under "Not on a task": "is waiting outside Ochre", Open thread, Link to a task (goes to Today's list of outside sessions), Dismiss |
| Kill an agent's `claude` process | | A red "stopped with an error" item with the exit code |
| Filters | | Needs you shows only amber items; Finished shows finished and failed; counts always reflect everything |

Checked 2026-11-26 in the headless dev host with a real agent on T-0013: the finished item and reply box, a Write prompt denied from the inbox (no file written), and a three-option question answered in place (the agent got "Green"). Captures in `captures/` (gitignored). Not yet walked: plan approval with feedback (the deny message is covered by `SessionLifecycleTests`), Ctrl+. with a waiting item, the tray tooltip, and two windows.
