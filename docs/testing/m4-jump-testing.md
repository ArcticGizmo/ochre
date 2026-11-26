# M4: jump to task

What changed: **Ctrl+Alt+Space**, from any app, brings up an Ochre window on the virtual desktop you're on (the one already there, or a new one) with a **Jump to task** picker. Inside Ochre, **Ctrl+K** opens the same picker. Type part of a title or an id; the arrows move, **Enter** shows the task in that window, **Ctrl+Enter** (or Ctrl+click) opens it in a window of its own, Esc closes. Open tasks come first, most recently touched first; done tasks appear once you search.

The shortcut is in Settings under **Jump to task**: change it (e.g. `Ctrl+Alt+J`), or clear it to turn it off; it applies when you save. It needs Ctrl, Alt or Win, and a letter, digit, F1 to F24 or Space. If another app already has it, Settings says so.

A dev host run headless or on another desktop doesn't register the shortcut, so a test run never takes yours; Settings says it isn't registered, and `ochre-cli dev jump` does what pressing it does. To try the real shortcut, run the dev host normally (`.\run.cmd`), with your installed Ochre quit (it holds the same shortcut).

## Checks

| Try | Before | After |
|---|---|---|
| With Ochre in the tray and another app focused, press **Ctrl+Alt+Space** | Nothing | An Ochre window comes to the front on this desktop with "Jump to task" open and the search focused |
| With no Ochre window on this desktop (they're on others, or all closed) | | A new window opens here with the picker; the windows on other desktops stay where they are, and you aren't switched |
| Type `exit` | | Only tasks whose title or id contains it; the first is highlighted |
| Down, then Enter | | The picker closes and the window shows that task (Tasks view) |
| Open the picker again, Ctrl+Enter on a task | | It opens in a window of its own (a window here already showing it comes forward; one on another desktop asks first) |
| In any window, **Ctrl+K** | | The picker |
| In a terminal tab, Ctrl+K | | The shell gets it (clears to the end of the line); no picker |
| Search for a done task's title | | It appears, after open ones |
| Settings: change the shortcut to `Ctrl+Alt+J`, Save | | Saves and closes; Ctrl+Alt+J now opens the picker and Ctrl+Alt+Space no longer does |
| Settings: enter `Shift+J`, Save | | "Include Ctrl, Alt or Win, so the shortcut doesn't take a key you type." Nothing saved |
| Settings: a shortcut another app holds | | Saved, with "Another app already uses ..." under it; pick another |
| Settings: clear the box, Save | | The shortcut is off; Ctrl+K still works |
| Quit and start Ochre | | The shortcut from Settings works again (`jumpHotkey` in `settings.json`) |

Automated: `HotkeyTests` (reading a shortcut in any case and order, writing it one way, and refusing ones it couldn't register or that would take typing).
