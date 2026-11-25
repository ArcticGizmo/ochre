# Security

Nidus runs agents that can execute anything, holds Jira/GitHub tokens, persists terminal scrollback and transcripts, renders untrusted text (agent output, Jira tickets, web pages) and will host third-party code. Most of the risk comes from what the app *does*, not the stack.

## Assets

- The user's shell and filesystem (via agents and terminals)
- Claude credentials and config dirs
- Integration tokens (Jira, GitHub)
- Transcripts and scrollback (which routinely contain secrets)
- Task data (plans that agents act on)

## Threats and required mitigations

| # | Threat | Mitigation (required) |
|---|---|---|
| 1 | **Malicious or compromised plugin** gets the shell, tokens and credentials | Capability manifest + explicit consent; re-consent when capabilities grow; SHA-256 verified installs; out-of-process with resource limits (Job Object); plugin UI only in a sandboxed iframe; master kill switch; no silent plugin updates |
| 2 | **Prompt injection through the links hub** (Jira, PR comments, web pages) makes an agent run commands | `task fetch` output is labelled untrusted; never auto-start agents on imported tasks in bypass mode; Claude Code's permission modes stay in force |
| 3 | **Cross-agent contamination**: an injected agent rewrites the plan or adds links other agents follow | Agents write only to their bound task; cannot change state or delete links; every write logged with author; agent plan changes highlighted for review |
| 4 | **Local IPC abuse** by other local processes or by websites (DNS rebinding, CSRF against loopback) | Named pipes with a per-user ACL for CLI/MCP; no loopback HTTP unless unavoidable, and then a random token plus Host/Origin checks |
| 5 | **XSS in the UI becomes code execution** through the bridge | Renderer treated as untrusted; narrow typed bridge validated on the host; no host objects injected into script; strict CSP (no inline or remote script); markdown sanitised (DOMPurify); UI never loaded from a remote URL |
| 6 | **Secrets at rest** in settings, scrollback or logs | Tokens in Credential Manager / Keychain; scrollback encrypted (DPAPI) with per-tab opt-out; logs never contain tokens or message bodies |
| 7 | **Terminal escape sequences** (clipboard write, deceptive links, title spoofing) | OSC 52 off or prompt; OSC 8 links limited to http(s) and shown before opening; titles never trusted for decisions |
| 8 | **Embedded browser** content reaching the app or the user's identity | Separate WebView profile; no bridge in the preview pane; no auto-granted permissions; CDP opened on demand, random port, throwaway profile |
| 9 | **Dependency supply chain** (npm, NuGet) | Lockfiles; exact pinned versions; minimal dependencies; pnpm with install scripts disabled; no `npx`/`pnpm dlx` against unpinned packages; review new dependencies |
| 10 | **Update channel tampering** | Signed builds; published SHA-256 sums verified before install (Perch's `install.ps1` pattern) |
| 11 | **Untrusted images and files** decoding to huge allocations | Read headers first and cap pixel counts (Perch's `ImageHeader` / `BoundedBitmap` approach); size-cap file previews |

## Stack-specific notes

- **Web UI:** threat 5 is the defining risk; the mitigations there are not optional. With a system WebView, the browser engine is patched by the OS; Electron would make Chromium patching our job.
- **.NET host:** there is no in-process sandbox in modern .NET, so third-party code never loads into the host process. Per-host network egress control for plugins is unsolved (AppContainer breaks realistic plugins); treat any network capability as "can exfiltrate" when asking for consent.

## Endpoint security (EDR)

The development machine runs CrowdStrike Falcon and Microsoft Defender. An app that spawns shells, ConPTY hosts and child processes looks suspicious to behavioural heuristics, so:

- Sign binaries.
- Never ship or plant runnable executables in tests; plant empty files and assert path resolution instead.
- If EDR blocks something, report it; never reword or re-encode a command to get it through.
