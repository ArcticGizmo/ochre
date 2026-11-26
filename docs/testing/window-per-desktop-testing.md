# A window on the current virtual desktop from the tray

What changed: clicking the tray icon (or "Open Ochre", or launching `ochre` again) brings forward an Ochre window on the virtual desktop you're on. If the only windows are on other desktops, it opens a new window here rather than leaving you where you are. Every window shows the same tasks, agents and terminals (the host owns them); each keeps its own view and selection. This is the first step of the roadmap's "Multiple windows" (M4); windows don't yet remember their desktop across a restart.

Start the dev build without `OCHRE_DEV_DESKTOP` set (that setting keeps the old one-window behaviour, for test runs on another desktop).

## Checks

| Try | Before | After |
|---|---|---|
| Open Ochre on desktop 1, switch to desktop 2, click the tray icon | Nothing visible happened (the window stayed on desktop 1) | A new Ochre window opens on desktop 2 |
| Click the tray icon again on desktop 2 | | The desktop 2 window comes forward; no third window |
| Minimise the desktop 2 window, click the tray icon | | It's restored |
| Go back to desktop 1, click the tray icon | | The desktop 1 window comes forward |
| In the desktop 2 window open a terminal and type; look at it from desktop 1 | | Both windows show the same output; the Terminals count matches |
| On desktop 2 go to Tasks, on desktop 1 stay on Today | | Each window keeps its own view |
| Close the desktop 2 window | | Only that window closes; the tray and desktop 1 window carry on |
| `cli close`, then tray Quit | | Every window closes; Quit ends Ochre |
