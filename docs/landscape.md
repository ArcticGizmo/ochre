# Landscape (Nov 2026)

Other agent development environments and where Ochre differs. Most details come from vendor pages and vendor-written comparisons, so treat feature claims as self-reported. Researched 2026-11-25.

| Tool | Platform | Licence | Task model | Worktrees | Notable |
|---|---|---|---|---|---|
| Claude Code desktop | Win, mac | Proprietary | None (sessions sidebar) | Yes | Parallel sessions redesign (Apr 2026), panes, diff review, nested sessions |
| Claude Code agent view | CLI | Proprietary | None | — | `claude agents` TUI over background sessions (May 2026) |
| OpenAI Codex app | Win, mac | Proprietary | Threads + automations | Yes | Skills, background automations |
| Cursor 3 Agents Window | Win, mac, Linux | Proprietary | Plan mode → agents (manual) | Yes | Local/worktree/cloud/SSH, best-of-N |
| VS Code Agents Window | Win, mac, Linux | MIT core | Sessions | Yes | Agent host process; Claude via Agent SDK; extensions |
| JetBrains Air | mac, Win | Proprietary | Tasks to agents | Yes | ACP agents; Air Gateway for terminal agents |
| Google Antigravity | Win, mac, Linux | Proprietary | Agent Manager + inbox | Workspaces | Separate "mission control" window |
| Warp | Win, mac, Linux | MIT/AGPL (Apr 2026) | — | — | Terminal-first; Oz cloud agents |
| Kepler (GitKraken) | Win, mac, Linux | Proprietary, preview | Tasks from Jira/Linear/GitHub/GitLab; multi-repo | Yes | Agent Graph; Actions |
| Emdash | Win, mac, Linux | Apache-2.0 | Kanban + issue intake | Yes (local/SSH) | 30+ CLI agents, best-of-N |
| Orca | Win, mac, Linux | MIT | GitHub/Linear issues | Yes | 27+ agents, embedded browser, mobile |
| Nimbalyst (ex-Crystal) | Win, mac, Linux | Mixed | Kanban of sessions + planning docs | Optional | Markdown/diagram planning |
| Superset | mac | Apache-2.0 / ELv2 | None | Yes | Automation hooks; no Windows |
| Vibe Kanban | Web/CLI | Apache-2.0 | Issues, sub-issues, columns | Yes | Company reportedly shut down Apr 2026 (unverified) |
| Conductor | mac | Proprietary | Workspace per task | Yes | Acquired by Meta 4 Nov 2026; cloud shut down |
| Agor | Web | BSL 1.1 (reported) | Worktree cards on a 2D board; zones fire prompts | Yes, with port allocation | Spatial, multiplayer |
| Kiro | Win, mac, Linux | Proprietary | Spec → `tasks.md`, agent checks off | — | Hooks |
| Beads / Gas Town | CLI | OSS | Git-backed issue graph for agents | — | v1.0 Apr 2026 |

## Where Ochre differs

- **Tasks outlive sessions and worktrees.** Almost every tool models task = worktree = agent = PR, and the task ends when the PR merges. Ochre tasks are sagas with many agents, worktrees and days, and a lifecycle you own.
- **Observes what it didn't launch.** Most tools must start the agent themselves. Ochre shows externally started sessions read-only and binds them to tasks.
- **Composable.** Turn off worktrees, Jira, the terminal; the rest still works.
- **Windows-first.** Several competitors are mac-only or reached Windows late.
- **Volatility is a reason to own it:** Conductor was acquired and shut its cloud; Vibe Kanban's company reportedly shut down.

## Ideas worth borrowing

- Antigravity's **inbox** of pending approvals.
- Agor's **zones** (drop a worktree into a zone to fire a templated prompt) and per-worktree port allocation.
- Kiro's spec → **tasks.md** checklist flow.
- Kepler's **multi-repo tasks** and Actions.
- Beads as a possible task backend or `TaskSource`.

## Sources

- [GitKraken: 6 ADEs compared (Oct 2026)](https://www.gitkraken.com/blog/6-agentic-development-environments-compared-2026)
- [Paralect: Top agentic IDEs](https://www.paralect.com/stack/top-agentic-ides)
- [Nimbalyst vs Conductor vs Vibe Kanban](https://nimbalyst.com/compare/nimbalyst-vs-conductor-vs-vibe-kanban/)
- [Superset vs Orca](https://superset.sh/compare/superset-vs-orca)
- [JetBrains Air preview](https://blog.jetbrains.com/air/2026/03/air-launches-as-public-preview-a-new-wave-of-dev-tooling-built-on-26-years-of-experience/)
- [Google Antigravity](https://developers.googleblog.com/en/build-with-google-antigravity-our-new-agentic-development-platform/)
- [Codex app](https://openai.com/index/introducing-the-codex-app/)
- [Cursor 3.0](https://cursor.com/changelog/3-0)
- [Claude Code desktop redesign](https://claude.com/blog/claude-code-desktop-redesign)
- [Agent view in Claude Code](https://claude.com/blog/agent-view-in-claude-code)
- [VS Code Agent Host](https://code.visualstudio.com/blogs/2026/08/26/agent-host-architecture)
- [Warp open-source](https://www.warp.dev/newsroom/2026/4/28/warp-open-sources-its-agentic-development-environment)
- [Meta acquires Conductor](https://siliconangle.com/2026/11/04/meta-acquires-ai-coding-startup-conductor-whose-app-runs-several-coding-agents-at-once/)
- [Agor README](https://raw.githubusercontent.com/preset-io/agor/main/README.md)
- [Kiro specs](https://kiro.dev/docs/cli/v3/specs/)
- [Beads](https://github.com/steveyegge/beads)
