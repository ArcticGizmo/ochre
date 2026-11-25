# 0001: Tasks are app-local markdown files

- Status: accepted
- Date: 2026-11-25

## Context

Tasks need to outlive sessions and be readable by agents. Storing them in a repo would version them with the code, but many tasks span several repos, so no single repo owns them.

## Decision

Tasks live in the app data dir (`%APPDATA%\Ochre\tasks\`), one markdown file with YAML frontmatter per task. The host process is the only writer; agents and the UI change tasks through it.

## Consequences

- Tasks are not shared with teammates or versioned with code. Export or a repo-backed store can come later as a module.
- Agents can't just `cat` a task from the repo; they use the `ochre task` CLI/MCP, which also enforces write rules.
- Plain files keep the data usable without Ochre.
