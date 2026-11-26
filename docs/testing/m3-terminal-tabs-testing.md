# M3 slice 2: terminal tabs in the window

What changed: a **Terminals** tab in the title bar (with a count) shows every terminal as a tab, drawn with xterm.js. Terminals are opened from a dialog (task, folder, shell, optional startup command), can belong to a task, and are listed on that task. The host keeps the shells running with every window closed; a window that opens later replays the scrollback and carries on live (ADR 0011). Design: `docs/design/m3-terminals/`.

## Checks

| Try | Before | After |
|---|---|---|
| Click **Terminals** with none open | No such tab | "No terminals..." and a New terminal button |
| New terminal: Task none, folder empty, PowerShell, no command | | A tab named PowerShell in your home folder, with a prompt and a blinking cursor; typing works; the title-bar count says 1 |
| Open a task, **Open terminal**, startup command `pnpm dev` (or any long-running command) in one of its repos | | The dialog preselects the task and its first repo; the tab is named after the command and carries the task id chip; the server's output streams in |
| Hover the server's `http://localhost:...` URL | | The bar at the bottom shows the URL and "Ctrl+click to open..." |
| Ctrl+click it; middle click it | | Opens in your browser; middle click opens a new browser window. A plain click only selects |
| Print a link to a non-web target: `Write-Host ("$([char]27)]8;;file:///C:/Windows$([char]7)click$([char]27)]8;;$([char]7)")` | | "click" prints but isn't a link |
| Select text, Ctrl+C; then Ctrl+C with nothing selected; Ctrl+V | | Copies; interrupts the running command; pastes |
| Close every Ochre window (tray stays), wait a while, open the window, go to Terminals | | The server is still running; the tab shows everything it printed meanwhile and keeps streaming |
| Open the task | | A **Terminals** section lists the terminal (running, name, folder, shell) with Open, which jumps to its tab |
| Tab menu **⋯** > Attach to another task… / Detach | | The chip and the task lists follow |
| Type `exit` | | The tab's dot goes hollow, "exited N"; **Restart shell** starts a new prompt after the old output |
| Close a running tab (x) | | Confirm dialog says the shell and anything in it stop; confirming removes the tab and ends the server |
| Resize the window | | The shell reflows to the new width (`$Host.UI.RawUI.WindowSize` in pwsh shows it) |

Known limits for now: quitting Ochre ends every terminal and nothing comes back yet (slice 3). Switching tabs replays the tab's scrollback, so a very long one takes a moment to draw. With two windows on one terminal, the shell takes the size of the one last focused.
