# Decisions

Architecture decision records. One file per decision, numbered, never renumbered. Supersede rather than edit: a replaced decision gets `Status: superseded by NNNN`.

Template:

```markdown
# NNNN: Title

- Status: proposed | accepted | superseded by NNNN
- Date: YYYY-MM-DD

## Context
## Decision
## Consequences
```

| # | Decision | Status |
|---|---|---|
| [0001](0001-app-local-tasks.md) | Tasks are app-local markdown files | accepted |
| [0002](0002-stack-dotnet-host-web-ui.md) | .NET host with one web UI in a system WebView | accepted |
| [0003](0003-separate-repo-copy-and-own.md) | Separate repo; copy code from Perch and own it | accepted |
| [0004](0004-claude-code-only.md) | Claude Code only, behind an adapter seam | accepted |
| [0005](0005-jira-import-and-link.md) | Jira is import + link; tasks are a links hub | accepted |
| [0006](0006-hook-free-session-observation.md) | Observe sessions from Claude's own files, no hook | proposed |
| [0007](0007-modules-on-plugin-contracts.md) | First-party features are modules on the plugin contracts | accepted |
| [0008](0008-tray-host-process.md) | A background tray host owns all state; windows are views | accepted |
| [0009](0009-bridge-contract-codegen.md) | The bridge contract is declared once in C# and the TypeScript types are generated | accepted |
| [0010](0010-pre-allow-task-commands.md) | Agents Ochre starts may run the task commands without a permission prompt | accepted |
| [0011](0011-host-owned-terminals.md) | The host owns terminals, filters their output and keeps their scrollback | accepted |
| [0012](0012-module-contracts.md) | Module contracts, first cut | accepted |
| [0013](0013-attention-inbox-and-roost.md) | The attention inbox is kernel; the Roost grid and notifications are modules | accepted |
