# 0002: .NET host with one web UI in a system WebView

- Status: proposed (pending the M0 spike)
- Date: 2026-11-25

## Context

Nidus needs an editor, a terminal, user widgets, plugins and a preview pane. On the web these are mature libraries (Monaco, xterm.js, sandboxed iframes); natively in Avalonia they are either immature (terminal controls at 0.x) or impossible to sandbox (in-process plugins). Agents also write React/TypeScript more reliably than owner-drawn Avalonia. On the other hand, Perch's large, tested C# logic (session control, transcripts, git, recovery) is worth reusing, and that logic is UI-free.

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

To be filled in after M0.
