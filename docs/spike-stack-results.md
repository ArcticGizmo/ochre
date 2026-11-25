# M0 spike results

Date: 2026-11-25. Machine: the development laptop (Windows 11 Pro 26200, CrowdStrike Falcon and Defender running). Release builds, framework-dependent, .NET 10.0.15, WebView2 runtime from the OS. Toolchain: .NET SDK 10.0.404, Node 24.21.0, pnpm 12.3.4, TypeScript 7.0.2, Vite 8.3.8.

The spike code was deleted after M0; it is preserved at the git tag `spike-stack`. Raw lines were in `%LOCALAPPDATA%\Ochre-spike\timing.log`. To rerun, check out the tag, then: build the UI (`pnpm build` in `ui/`), build a shell in Release, then start it with `OCHRE_SPIKE_AUTOBENCH=1` (and optionally `OCHRE_SPIKE_DESKTOP=<virtual desktop guid>` to keep the window off the current desktop) and open its window by launching it a second time. `open-timing.ps1` times window open; `measure.ps1` samples memory; `wt-baseline.ps1` times the same throughput commands in Windows Terminal.

## Shells tried

| Shell | Status |
|---|---|
| WinForms + WebView2 | Works. Needed an MSBuild target to drop the package's WPF reference (WindowsBase conflict warning). |
| Avalonia 12.1.6 + `Avalonia.Controls.WebView` 12.1.1 (MIT) | Works after two fixes: an app manifest with a Windows 10 `supportedOS` entry (else `NativeControlHost` can't create its child window and the app crashes), and built-in COM interop on. `NativeWebView` has no host-to-page message or virtual-host API, so the shell wraps the underlying `CoreWebView2` from `IWindowsWebView2PlatformHandle` and uses the same page code as WinForms. On macOS that path doesn't exist; host-to-page would go through `InvokeScript`. |
| Photino.NET | Not tried; plan said only if both of the above were marginal. |

## Measurements

| Metric | Target | WinForms + WebView2 | Avalonia |
|---|---|---|---|
| Cold start to tray | < 1.5 s | 0.39 to 0.50 s | 0.66 to 0.84 s |
| Tray idle, no window (5 min, working set) | < 120 MB | 48 MB (13 MB private) | 99 MB (53 MB private) |
| One window open, idle 60 s (private bytes, host + WebView, excl. pwsh/conhost) | < 350 MB | 196 MB | 275 MB |
| Same, summed working sets (overcounts pages shared by the 6 WebView processes) | | 438 to 448 MB | 545 MB |
| Host process working set, window open | | 71 MB | 170 to 187 MB |
| After the full throughput bench (private, incl. pwsh) | | 340 MB | |
| First window open to usable UI, before fixes | | 2.6 to 2.9 s | 2.7 to 3.1 s |
| First window open, SmartScreen off + lazy Monaco | | 0.85 s | 1.12 to 1.19 s |
| Reopen after close (WebView torn down) | < 500 ms warm | 0.76 s | 0.76 to 0.87 s |
| Re-show after hide (WebView kept) | < 500 ms warm | 12 to 92 ms | 21 to 72 ms |
| Page share of open (navigation start to ready) | | 200 to 260 ms | 104 to 158 ms |
| Bridge round trip, 10k sequential | < 1 ms median | 0.10 ms median, 0.40 ms p99 | 0.10 ms median, 0.40 ms p99 |
| Simulated typing (input to echo) | < 30 ms | 1.2 ms median, 10.9 ms p99 | 1.2 ms median, 11.9 ms p99 |
| Serialize 913 KB scrollback | | 30 ms | 47 ms |
| Package size (win-x64, framework-dependent) | record | 17 MB (15 MB of it UI) | 45 MB (15 MB of it UI) |

### Terminal throughput

Output from PowerShell through ConPTY to xterm.js, with the terminal tab visible (WinForms) or on another virtual desktop with Chromium's occlusion throttling switched off (Avalonia). "Delivered" is command sent to last output; xterm finished parsing within 1 ms of that in every run.

| Run | Per read | Coalesced 16 ms |
|---|---|---|
| WinForms: recursive listing (23.7 M chars) | 78.8 s, 437k messages, worst frame gap 34 ms | 54.6 s, 2.4k messages, worst frame gap 17 ms |
| WinForms: cat 50 MB | 47.0 s, 556k messages, 33 ms | 32.6 s, 1.4k messages, 17 ms |
| Avalonia: recursive listing | 82.9 s, 437k messages, 34 ms | 57.8 s, 2.6k messages, 34 ms |
| Avalonia: cat 50 MB | 43.1 s, 562k messages, 33 ms | 31.6 s, 1.4k messages, 18 ms |
| Windows Terminal (same commands, `Measure-Command` around `Out-Host`) | listing 48.5 s, cat 50 MB 27.9 s | |
| Ochre vs Windows Terminal | listing 1.6 to 1.7x, cat 1.5 to 1.7x | listing 1.13 to 1.19x, cat 1.13 to 1.17x |
| PowerShell with no terminal (`Out-String`) | listing 31.6 s | |

No long tasks (> 50 ms) were recorded in any run, so the UI never froze. ConPTY emits roughly one message per line, so per-read delivery means hundreds of thousands of `postMessage` calls; coalescing on a 16 ms timer cuts time by about 30% and keeps frames under one vsync.

### Window open

Every cold page load stalled for a fixed ~2 s inside HTML parsing (`index.html` arrived in 6 ms, DOM interactive at ~2,020 ms). A reserved `.example` virtual host name made no difference; turning off WebView2's SmartScreen reputation check (`IsReputationCheckingRequired = false`) removed the stall entirely. That's safe here because the UI is only served from the app folder and every other navigation is refused; links open in the real browser, which does its own check.

What's left of a cold open is mostly WebView2 starting its browser process and controller (~550 ms); the page itself is ready 100 to 260 ms after navigation starts. Lazy-loading Monaco (4.5 MB entry bundle down to 579 KB) is right regardless, but it wasn't the cause.

A reopen under 500 ms needs the WebView kept alive: hiding instead of closing re-shows in under 100 ms, at the cost of keeping the WebView processes' memory while the window is hidden.

## Notes

- **Bridge typing.** Hand-written TS mirrors of the C# records were quick to write but already drifted once during the spike (a new message needed edits in three places). Generating the TS types from the C# records is worth it for M1.
- **Hot reload.** Not exercised; `OCHRE_SPIKE_DEV_URL` points the WebView at a Vite dev server, and the build-time-only CSP keeps dev mode working.
- **EDR.** No blocks or prompts from launching the shells, spawning pwsh under ConPTY, or the WebView2 processes.
- **Monaco under strict CSP.** Works with `script-src 'self'` and `worker-src 'self'`; needs `style-src 'unsafe-inline'` for its injected styles.

## Verdict against the targets

- Met: cold start, tray idle, one-window memory (on private bytes; summed working sets exceed 350 MB but double-count shared pages), bridge round trip, typing latency, no UI freeze under heavy output.
- Met only with the WebView kept alive: window open under 500 ms. A torn-down WebView reopens in ~0.76 s.
- Met: terminal throughput within 2x of Windows Terminal. Coalesced output is 13 to 19% slower than Windows Terminal on the same commands; per-read delivery is 1.5 to 1.7x.

## Recommendation

**Accept option B** (ADR 0002): the bridge carries terminal output comfortably once coalesced, and memory and startup are within target.

**Shell: raw WebView2 in a minimal WinForms host, behind a small shell seam** (tray, window, post/receive message) in `Ochre.Platform.Windows`. Reasons:

- Everything users feel (bridge, typing, throughput, page load) is identical across the two shells, because both are WebView2 underneath.
- Avalonia adds cost where the shell itself shows: +50 MB at tray idle, +80 to 100 MB with a window, +0.25 s cold start, +0.3 s first open, +28 MB package, and two crash-on-open gotchas.
- Avalonia's cross-platform value didn't materialise: on Windows the shell had to reach past `NativeWebView` to the raw `CoreWebView2` for message posting and the virtual host, so a macOS head would need its own path anyway.
- WinForms here is only a tray icon and a window frame; no UI is drawn with it, so it's a thin, replaceable layer.

macOS gets its own implementation of the same seam later (WKWebView, natively or via Avalonia then).

Carry into M1: coalesce terminal output (16 ms), `IsReputationCheckingRequired = false` for the app's own page, lazy-load Monaco, a `.example` virtual host, and generated TS types for the bridge.
