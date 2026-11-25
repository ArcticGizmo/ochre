# 0002: .NET host with one web UI in a system WebView

- Status: accepted (2026-11-25, after the M0 spike)
- Date: 2026-11-25

## Context

Ochre needs an editor, a terminal, user widgets, plugins and a preview pane. On the web these are mature libraries (Monaco, xterm.js, sandboxed iframes); natively in Avalonia they are either immature (terminal controls at 0.x) or impossible to sandbox (in-process plugins). Agents also write React/TypeScript more reliably than owner-drawn Avalonia. On the other hand, Perch's large, tested C# logic (session control, transcripts, git, recovery) is worth reusing, and that logic is UI-free.

Options considered:

| Option | Summary | Verdict |
|---|---|---|
| A. Avalonia + web islands | Native shell, WebView per pane | Two UI models, airspace issues, weak plugin UI |
| B. .NET host + full web UI | .NET owns tray, PTYs, sessions, git; one web app in a WebView | **Proposed** |
| C. Electron | Bundled Chromium; Node backend or .NET sidecar | Loses reuse or adds a second runtime; always-on Chromium |
| D. Tauri | System WebView; Rust backend | Rust rewrite |

## Decision

Option B, subject to the spike's measurements ([spike-stack.md](../spike-stack.md)): a .NET 10 host process with a tray icon, and a TypeScript/React UI in WebView2 (Windows) / WKWebView (macOS). The shell (Avalonia, Photino.NET or raw WebView2) is chosen by the spike.

## Consequences

- Perch's logic is reused; Perch's UI is not (it becomes the behavioural spec).
- XSS in the UI could escalate through the bridge, so the renderer is treated as untrusted ([security.md](../security.md), threat 5).
- Two toolchains (dotnet + pnpm) in one repo.
- macOS uses WebKit, so the UI must be tested on both engines.

## Spike results

Measured 2026-11-25 on the development laptop with WinForms + WebView2 and Avalonia 12 + `Avalonia.Controls.WebView`; full numbers and method in [spike-stack-results.md](../spike-stack-results.md).

| Metric | Target | WinForms + WebView2 | Avalonia |
|---|---|---|---|
| Cold start to tray | < 1.5 s | 0.4 to 0.5 s | 0.6 to 0.8 s |
| Tray idle (5 min) | < 120 MB | 48 MB | 99 MB |
| One window idle, host + WebView private bytes | < 350 MB | 196 MB | 275 MB |
| First window open | | 0.85 s | 1.1 to 1.2 s |
| Reopen: torn down / hidden | < 500 ms warm | 0.76 s / < 0.1 s | 0.76 to 0.87 s / < 0.1 s |
| Bridge round trip (10k) | < 1 ms median | 0.10 ms | 0.10 ms |
| Typing (input to echo) | < 30 ms | 1.2 ms median | 1.2 ms median |
| 50 MB through the terminal, coalesced 16 ms | no freeze; within 2x of Windows Terminal | 32.6 s, worst frame 17 ms (Windows Terminal: 27.9 s) | 31.6 s, worst frame 18 ms |
| Package (win-x64, framework-dependent) | record | 17 MB | 45 MB |

Findings that shape M1: terminal output must be coalesced (per-read delivery is ~30% slower and sends ~500k messages for 50 MB); WebView2's SmartScreen check added a fixed ~2 s to every cold page load and is turned off for the app's own page; Avalonia's `NativeWebView` has no host-to-page message or virtual-host API, so on Windows it ends up using the raw `CoreWebView2` anyway.

Outcome: option B accepted. The Windows shell is raw WebView2 in a minimal WinForms host (tray icon and window frame only), behind a shell seam in `Ochre.Platform.Windows`; macOS implements the same seam later. Avalonia was rejected for the shell: it matched WebView2 wherever the WebView does the work and cost more wherever the shell shows (memory, startup, package size), and its WebView control couldn't post to the page or map a local folder, so it gave no cross-platform head start.
