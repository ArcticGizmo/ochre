# M5 slice 4: notifications

The `notifications` module (on by default) shows a Windows notification when an agent needs you or finishes, grouped under its task, and acts on the buttons. Built on the spike (`docs/spike-notifications-results.md`).

## Real notifications (by hand)

These put notifications on your screen, so run them yourself: quit the installed Ochre if it's running, then `.\run.cmd` without the headless variables (or headless with `$env:OCHRE_DEV_NOTIFY='1'`). Notifications show as "Ochre (dev)".

| Try | Before | After |
|---|---|---|
| Start an agent on a task, switch to another app, and have it ask to write a file | Nothing outside Ochre | A notification under the header "T-nnnn title": "role wants to use Write", what it would write, Allow (green), Deny (red), Open. It stays on screen until you act (reminder) |
| Click Allow | | The agent carries on; the inbox item goes |
| Same, but with Ochre's window focused on the inbox or that agent's thread | | No notification; the item is in front of you |
| A notification showing, then open the inbox in Ochre | | The notification goes, and doesn't come back when you switch away |
| An agent asks a single question with up to five options | | A choice box and Answer; the agent gets the option you chose |
| A question with more options or several parts | | The question and Open only |
| An agent in plan mode presents a plan | | Approve, a text box with Reject beside it; Reject with text sends Claude your text |
| An agent finishes its turn | | "role finished" with the reply's last line, a reply box with Send, and Open; Send makes your text its next prompt |
| Kill an agent's `claude` process | | "role stopped with an error" with the exit code, and Open |
| Click a notification's body, its header, or Open | | A window on the current desktop shows the task (a window showing it on another desktop is brought here); an outside session opens a window |
| Click Allow on a notification after answering the prompt in Ochre | | Windows takes the notification down, then a new one says "role: not done / that is no longer waiting" (nothing is allowed) |
| Quit Ochre with notifications showing | | They're all gone from the notification centre |
| An outside `claude` waiting | | No notification (answered where it runs); it's still in the inbox |
| Turn the module off in Settings and restart | | No notifications; the inbox works as before |
| Click a notification in the notification centre rather than the pop-up | | Same as the pop-up (not checked in the spike; confirm here) |

## Headless (driven by the CLI)

A headless dev run never shows notifications; it records them. `ochre-cli dev notifications` lists what would be showing; `ochre-cli dev notify <tag> <action> [key=value ...]` presses a button (`allow`, `deny`, `answer choice=1`, `approve`, `reject text=...`, `send text=...`, `open`).

Checked 2026-11-26 in the headless dev host with a real agent on T-0013: a Write prompt recorded with Allow/Deny/Open and denied from it (no file written); the finished notification's reply sent as the next prompt; a three-option question answered with `choice=2` (the agent got "Blue"); a window reporting the inbox in focus took the notification away, and it didn't come back after looking away. Not yet checked: everything in the table above, which needs real notifications on the screen.
