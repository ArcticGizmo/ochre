# M3 exit run: terminal tabs

Exit criterion (roadmap): run a dev server in a tab, close all windows, reopen: still running. Quit and relaunch: tabs restored with scrollback.

Walked 2026-11-26 in the dev window (on another virtual desktop, driven with `cli dev eval` and checked with `cli dev capture`). The "dev server" was a child `pwsh` printing a numbered line and a `http://localhost:5173/` link every second, with a marker in its command line so its process could be found.

| Step | Result |
|---|---|
| Open a terminal on T-0001 in the repo with the server as its startup command | The tab is named after the command, carries the T-0001 chip, and streams ticks; T-0001 lists it under Terminals |
| Close every Ochre window for 15 s, then open the window and go to Terminals | Still running: the last line went from tick 36 to tick 72, and the tab showed everything printed meanwhile |
| Quit Ochre from the tray | The server's process ended (no process with the marker left) |
| Look at `%LOCALAPPDATA%\Ochre-dev\terminals\` | `terminals.json` lists the tab; its `.scrollback` doesn't contain the marker text (encrypted) |
| Start Ochre, open Terminals | The tab is back on T-0001 in the same folder: ticks 1 to 8, the blue "restored" divider, a fresh prompt, and a banner saying the command wasn't started again |
| **Run it again** | Ticks start again from 1; the banner goes |
| Close the tab | Its process ends |

Per-slice checks: `m3-terminals-host-testing.md` (host and filter), `m3-terminal-tabs-testing.md` (the window), `m3-restore-testing.md` (restore).

Found on the way:

- ConPTY clears the screen when a shell starts, which wiped restored output; xterm's `scrollOnEraseInDisplay` keeps cleared screens in the scrollback.
- ConPTY places its cursor absolutely, so a replay can end mid-screen; the restore divider clears and homes before drawing.
- Gating input on `document.hasFocus()` alone could silently drop typing; input now also counts as the user's for a second after a key, paste or click in the terminal.
- ConPTY passes OSC 52 (clipboard write) through, so the host filter is the control, not ConPTY.

Not done, by plan: "view as terminal" for an agent's thread; split panes; search in scrollback.
