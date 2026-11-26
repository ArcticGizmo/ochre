# M4 slice 4: multiple windows

What changed: any task can have a window of its own. Right-click a task in the list and choose **Open in new window**, click the new-window button beside a task's Park/Resume, or run `ochre-cli open T-0042`. The window opens on the virtual desktop you're on and shows that task; its title bar reads `T-0042 <title> - Ochre`, so windows can be told apart in Alt+Tab and Task View. If a window on this desktop already shows the task, it comes forward instead. If the only one showing it is on another desktop, Ochre asks whether to bring that window here or open another one here; it never switches desktops.

When you quit from the tray, every open window is saved to `%LOCALAPPDATA%\Ochre\windows.json` (the dev profile: `%LOCALAPPDATA%\Ochre-dev\`): its task, its size and place, whether it was maximised, and its virtual desktop. The next start reopens each on its own desktop, on the same monitor, showing the same task (on the Tasks view; an agent thread that was open isn't reopened). A desktop that's gone falls back to the current one; a monitor that's gone puts the window on the primary at the same distance from the corner. Closing a window yourself removes it from the layout. Windows shutting down saves the layout the same way quitting does.

`OCHRE_HIDE_ON_CLOSE` is gone: closing a window closes it.

The two-desktop checks below need real desktops, so they're done by hand with the dev host run normally (not headless): `.\run.cmd`. The rest can also be driven headless with `ochre-cli dev windows`, `dev eval --window <id>` and `dev capture --window <id>`.

## Checks

| Try | Before | After |
|---|---|---|
| Right-click a task in the Tasks list | The browser's menu (dev) or nothing | A small menu: **Open**, **Open in new window** |
| Choose **Open in new window** | | A second window opens on this desktop, on the Tasks view with that task selected. Its title bar starts with the task id and title |
| On a task, click the new-window button (two overlapping squares, beside Park/Resume) | | Same: a new window for that task. The window you clicked in keeps showing it too |
| In the first window, choose **Open in new window** on the same task again | | No third window: the one already showing it comes forward |
| `ochre-cli open T-0042` in a terminal on this desktop | `open` took no task | Prints `opened` and a window for the task appears; run it again and it prints `focused` |
| `ochre-cli open T-9999` | | `error: no task T-9999` |
| Quit Ochre, then `ochre-cli open T-0042` | | Ochre starts in the tray, reopens the saved windows, then opens the task's window (no extra empty window) |
| Move the task window to desktop 2 (Win+Tab, drag it). Back on desktop 1, choose **Open in new window** on that task | | A dialog: "T-0042 is open on another desktop", with Cancel, **Open another window here** and **Bring that window here**. You stay on desktop 1 throughout |
| Choose **Bring that window here** | | The window from desktop 2 appears on desktop 1 and takes focus; desktop 2 no longer has it |
| Repeat and choose **Open another window here** | | A second window for the task opens on desktop 1; the one on desktop 2 stays |
| From a terminal on desktop 1: `ochre-cli open T-0042` while only desktop 2 shows it | | Asks "[b] bring that window here, [n] open another window here"; `b` prints `moved`, `n` prints `opened`, anything else `cancelled`. With input redirected, it explains `--bring` and `--new` and exits 1 |
| **Exit criterion**: open task A in a window on desktop 1 and task B in a window on desktop 2. Size and place them differently, maximise one. Quit from the tray, then start Ochre | | Each window reopens on its own desktop, at its size and place (the maximised one maximised), showing its task. Neither switches you to another desktop |
| Close one of them with its X, quit, start again | | Only the other comes back |
| Start with `ochre --tray`, then quit from the tray without opening a window | | The next start still reopens the saved windows (a tray-only run doesn't overwrite them) |
| Remove desktop 2 while Ochre isn't running, then start | | The window that was there opens on the current desktop |
| Put a window on a second monitor, quit, unplug the monitor, start | | It opens on the primary monitor, the same distance from the nearest corner, shrunk if it doesn't fit |
| Delete a task that a saved window showed, then start | | That window opens on Today instead |
| Edit `windows.json` to `not json`, then start | | Ochre starts with one window as usual; quitting writes a fresh layout |

Automated: `PlacementMathTests` (ported from Perch), and `WindowLayoutTests` (a missing, damaged or partial `windows.json` reads as no windows and never throws; bad entries are skipped; a save round-trips; a window comes back exactly on an unchanged monitor, on the primary with the same corner distance when its monitor is gone, shrunk when too big, and keeps its size in DIPs when the scale changes).
