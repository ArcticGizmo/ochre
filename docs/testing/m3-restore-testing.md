# M3 slice 3: terminal tabs come back after a quit

What changed: Ochre saves its terminal tabs in `%LOCALAPPDATA%\Ochre\terminals\` (`Ochre-dev` for the dev build): `terminals.json` lists each tab (shell, folder, task, startup command, title), and each tab that keeps its scrollback has `<id>.scrollback`, encrypted with DPAPI for your Windows account. The list is saved on every change, scrollback every 30 s while it changes, and both on quit and when Windows shuts down. On the next start every tab comes back with its old output, a "restored" divider and a fresh shell in its last folder. A startup command is offered again, never run by itself.

## Checks

| Try | Before | After |
|---|---|---|
| Open a tab on a task with a startup command that keeps printing (a dev server), wait, quit Ochre from the tray | Every terminal was gone for good | The server's process has ended (no orphan in Task Manager) |
| Look in the terminals folder | | `terminals.json` names the tab; `<id>.scrollback` is unreadable bytes: search it for a word from the output and it isn't there |
| Start Ochre, open Terminals | | The tab is back with its task chip; the old output, then a blue `──── restored · saved <time> · new shell <time> ────` line, then a new prompt in the same folder. A banner says the command was not started again |
| **Run it again** | | The command runs; the banner goes |
| Quit and start again without running it; **Dismiss** | | The banner goes; nothing runs |
| New terminal with "Keep scrollback when Ochre quits" unticked, print something, quit, start | | The tab comes back (same shell, folder, task) with only the divider and a new prompt; no `.scrollback` file was written for it |
| Tab menu **⋯** > untick "Keep scrollback when Ochre quits" on a tab that had it | | Its `.scrollback` file is deleted within a second |
| Close a tab, quit, start | | It doesn't come back, and its `.scrollback` is gone |
| In a full-screen program (e.g. `vim`) quit Ochre, start again | | The restored tab is readable: normal screen, cursor visible, divider, prompt |
| Type `cls` in pwsh, scroll up | Earlier output was lost | It's in the scrollback |
| Break `terminals.json` by hand (`{ x`), start Ochre | | No tabs restored; the log says it couldn't be read; the file is left as you broke it |

Automated: `TerminalPersistenceTests` (round trip, scrollback never in plain text, hostile ids in the list, unreadable lists left alone, restore with the command offered, close and keep-off deleting scrollback).
