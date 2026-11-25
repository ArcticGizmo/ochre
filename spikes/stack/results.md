# M0 spike results

Date: 2026-11-25. Machine: the development laptop (Windows 11 Pro 26200, CrowdStrike Falcon and Defender running). Release builds, framework-dependent, .NET 10.0.15, WebView2 runtime from the OS. Toolchain: .NET SDK 10.0.404, Node 24.21.0, pnpm 12.3.4, TypeScript 7.0.2, Vite 8.3.8.

Raw lines are in `%LOCALAPPDATA%\Ochre-spike\timing.log`. To rerun: build the UI (`pnpm build` in `ui/`), build a shell in Release, then start it with `OCHRE_SPIKE_AUTOBENCH=1` (and optionally `OCHRE_SPIKE_DESKTOP=<virtual desktop guid>` to keep the window off the current desktop) and open its window by launching it a second time. `open-timing.ps1` times window open; `measure.ps1` samples memory; `wt-baseline.ps1` times the same throughput commands in Windows Terminal.

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
| Tray idle, no window (5 min) | < 120 MB | TODO | TODO |
| One window open, idle (private bytes, host + WebView, excl. pwsh/conhost) | < 350 MB | 238 MB | TODO |
| Same, summed working sets (overcounts shared pages) | | 493 MB | TODO |
| Host process working set, window open | | 69 to 72 MB | 187 MB |
| First window open to usable UI (eager Monaco, 4.5 MB entry) | < 500 ms warm | 2.87 to 2.93 s | 3.13 s |
| First window open (lazy Monaco, 579 KB entry) | | TODO | TODO |
| Reopen after close / re-show after hide | < 500 ms | TODO | TODO |
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
| Windows Terminal (same commands) | TODO | |
| PowerShell with no terminal (`Out-String`) | listing 31.6 s | |

No long tasks (> 50 ms) were recorded in any run, so the UI never froze. ConPTY emits roughly one message per line, so per-read delivery means hundreds of thousands of `postMessage` calls; coalescing on a 16 ms timer cuts time by about 30% and keeps frames under one vsync.

## Notes

- **Bridge typing.** Hand-written TS mirrors of the C# records were quick to write but already drifted once during the spike (a new message needed edits in three places). Generating the TS types from the C# records is worth it for M1.
- **Hot reload.** Not exercised; `OCHRE_SPIKE_DEV_URL` points the WebView at a Vite dev server, and the build-time-only CSP keeps dev mode working.
- **EDR.** No blocks or prompts from launching the shells, spawning pwsh under ConPTY, or the WebView2 processes.
- **Monaco under strict CSP.** Works with `script-src 'self'` and `worker-src 'self'`; needs `style-src 'unsafe-inline'` for its injected styles.

## Decision

TODO after the remaining measurements.
