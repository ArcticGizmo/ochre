# Release, install and update

How Ochre is built, released, installed and updated ([ADR 0014](../decisions/0014-velopack-distribution.md)). Everything below the first two rows installs Ochre on the machine (Start menu entry, PATH, a running tray icon), so it's for the user to run.

| Try | Before | After |
|---|---|---|
| `.\publish.ps1` | No way to build a release | `releases\` holds `OchreApp-win-Setup.exe`, `OchreApp-win-Portable.zip`, the update feed (`OchreApp-0.1.0-full.nupkg`, `RELEASES`, `releases.win.json`, `assets.win.json`) and `SHA256SUMS.txt`; `publish\` holds `ochre.exe`, `ochre.com`, `wwwroot\` and `WebView2Loader.dll` |
| `publish\ochre.com status` with nothing installed | | `not running (profile default)`: a release build uses the normal profile, never the dev one |
| Run `releases\OchreApp-win-Setup.exe` | | Installs without asking for admin, to `%LocalAppData%\OchreApp`, adds **Ochre** to the Start menu (no desktop shortcut) and starts it; the tray, window, taskbar and Start menu show the Ochre icon |
| A new terminal: `ochre status` | `ochre` not found | `running 0.1.0 profile=default ...` |
| Run the dev build (`run.cmd`) at the same time | | Both run: separate tasks, settings, windows and tray icons; the dev one says DEV |
| An agent started from the installed Ochre runs `ochre task show` | | Works without a prompt, through `ochre.com` beside the installed exe |
| Settings in the installed copy | | **Start Ochre when you sign in** (off), and Updates: "Version 0.1.0" with **Check for updates**. The dev build instead says "This copy isn't installed, so it doesn't update itself" and has no sign-in option |
| Tick **Start Ochre when you sign in**, Save; sign out and in | | Ochre is in the tray with no window (`HKCU\...\Run`, value `Ochre`, runs `ochre.exe --tray`) |
| Build a newer version (`.\publish.ps1 -Version 0.1.1`), set `OCHRE_UPDATE_FEED` to the `releases` folder for the installed Ochre (user environment variable, then quit and start Ochre), **Check for updates** | | "Version 0.1.1 is ready to install from the title bar"; the title bar's version becomes **Update to v0.1.1** |
| Click **Update to v0.1.1** | | A dialog says Ochre restarts, agents come back dormant and terminals with their scrollback; **Update and restart** shows the download percentage, then Ochre closes and comes back on 0.1.1 with its windows |
| With no feed set, leave Ochre running a minute after start | | It has asked GitHub (Settings shows "Up to date, checked ..." or the newer version) |
| Push a tag `v0.1.1` | | The Release workflow tests, builds and publishes a GitHub release with the installer, feed and `SHA256SUMS.txt`; an installed 0.1.0 finds it |
| `irm https://raw.githubusercontent.com/ArcticGizmo/ochre/main/install.ps1 \| iex` once a release exists and `install.ps1` is on main | | Downloads the installer, prints "SHA-256 verified", installs and starts Ochre |
| Uninstall from Settings, Apps | | `%LocalAppData%\OchreApp` is gone; the PATH entry, sign-in start and notification identity are removed; tasks in `%APPDATA%\Ochre` and data in `%LOCALAPPDATA%\Ochre` stay |
| A pull request | No CI | The CI workflow builds the UI and the solution, checks the bridge types and runs the tests |

Checked 2026-11-26: `publish.ps1` end to end on this machine (the CLI fell back to a single file: no C++ build tools here; CI has them), the published `ochre.com status`, a dev run (passes through Velopack's start-up check; Settings shows the not-installed note and no sign-in option), and the tests (the PATH editor's copied from Perch). Not yet run: installing, updating, the workflows (they run on the first pull request and the first `v*` tag) and `install.ps1`.
