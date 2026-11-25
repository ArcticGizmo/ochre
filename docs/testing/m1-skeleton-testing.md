# M1 slice 1: skeleton and tray host

What changed: the first real code. `Ochre.sln` with `Ochre.Core` (profile, paths, log, bridge dispatcher, shell seam, single-instance channel, safe URL opening), `Ochre.Platform.Windows` (tray + WebView2 window on WinForms), `Ochre.Host` (the `ochre` exe) and `tests/Ochre.Tests` (URL safety tests copied from Perch). The UI under `ui/` is a placeholder layout that asks the host for its version over the bridge.

## Setup

1. `pnpm install` and `pnpm build` in `ui/`.
2. `dotnet build Ochre.slnx` (Debug builds run as the `dev` profile: data under `%APPDATA%\Ochre-dev` and `%LOCALAPPDATA%\Ochre-dev`, and their own pipe, so they never touch a day-to-day install).
3. Optional: set `OCHRE_DEV_DESKTOP` to a virtual desktop GUID before the first launch so the window opens there, unfocused.

Log: `%LOCALAPPDATA%\Ochre-dev\logs\ochre-<date>.log`.

## Checks

| Try | Before | After |
|---|---|---|
| Run `src\Ochre.Host\bin\Debug\net10.0-windows\ochre.exe` | Nothing to run | Ochre tray icon (orange disc) appears and a window opens titled "Ochre (dev)"; the header shows "Ochre", a DEV badge and v0.1.0 |
| Read the log | | `starting 0.1.0 profile=dev`, `tray ready` (about 0.5 s) and `window ready` (under 1 s) |
| Run `ochre.exe` again | | No second tray icon; the existing window comes forward |
| Close the window with its X | | Tray icon stays; the process keeps running |
| Left-click the tray icon | | The window reopens (about 0.8 s) |
| `ochre.exe --close-window` | | The running instance's window closes |
| `ochre.exe --tray` with nothing running | | Tray icon only, no window |
| Tray menu > Quit | | Icon and process go away |
| Set `OCHRE_HIDE_ON_CLOSE=1`, start, close the window, click the tray icon | | Reopens near-instantly (the WebView was kept) |
| Run `dotnet test` | | 29 passed |
| In the window, press F12 (Debug build) | | DevTools open; console shows no CSP errors |
