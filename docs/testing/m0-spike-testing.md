# M0 stack spike: manual testing

What changed: a throwaway spike under `spikes/stack/` with two shells (WinForms + WebView2, Avalonia) over a shared core and web UI, used to settle ADR 0002. Nothing here ships; this doc is how to rerun the measurements.

## Setup

The spike was deleted after M0, so start from its tag: `git worktree add ../ochre-spike spike-stack` and run the steps below inside that checkout.

1. `pnpm install` then `pnpm build` in `spikes/stack/ui/`.
2. `dotnet build spikes/stack/Shell.WebView2 -c Release` (and `Shell.Avalonia` to compare).
3. Optional: set `OCHRE_SPIKE_DESKTOP` to a virtual desktop GUID (from `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops`) so windows open there, unfocused, instead of on your current desktop.

Results land in `%LOCALAPPDATA%\Ochre-spike\timing.log`.

## Checks

| Try | Expected |
|---|---|
| Start `ochre-spike-webview2.exe` | Tray icon appears; `cold-start-to-tray` under 1.5 s in the log; no window |
| Start it a second time | The running instance opens a window; the second process exits; `window-open-to-ui-ready` about 0.85 s cold |
| Start it again with `--close`, then plain again | Window closes, then reopens in about 0.76 s |
| Repeat with `OCHRE_SPIKE_HIDE_ON_CLOSE=1` on the first launch | Re-show after `--close` under 100 ms |
| Terminal tab: type `dir` | Output appears instantly; PowerShell is interactive |
| Editor tab: open an absolute path to a `.cs` file, tick "Diff against disk", edit a line | Monaco loads on first visit to the tab; the diff shows the edit |
| Task list | Three sample tasks; clicking one shows its plan with checkboxes |
| First launch with `OCHRE_SPIKE_AUTOBENCH=1`, then open the window | The bench runs unattended (about 3 minutes) and logs echo, typing, throughput (per read and coalesced) and serialize lines, ending `autobench: done` |
| `.\spikes\stack\measure.ps1 -ProcessName ochre-spike-webview2` | Memory for host + WebView with and without the terminal's shell processes |
| `.\spikes\stack\open-timing.ps1 -Shell webview2` | First open, close/reopen and hide/re-show timings for both close modes |
| `.\spikes\stack\wt-baseline.ps1` (opens a visible Windows Terminal tab) | Appends listing and 50 MB cat timings to `%LOCALAPPDATA%\Ochre-spike\wt-baseline.log` |

## Before and after

- Before SmartScreen was turned off for the app page: first window open 2.6 to 2.9 s, with the page's HTML parse stalled ~2 s. After: 0.85 s.
- Before coalescing: 50 MB cat took 47 s in ~556k messages. After (16 ms): 32.6 s in ~1.4k messages.
