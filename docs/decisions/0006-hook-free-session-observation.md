# 0006: Observe sessions from Claude's own files, no hook

- Status: proposed
- Date: 2026-11-25

## Context

Perch observes sessions partly through `perch-hook`, which also launches the `perch` tray by process name. Perch has since moved most signals to Claude-native sources (session files, transcripts, `history.jsonl`). Nidus must work whether or not Perch is installed.

## Decision

Nidus observes externally started sessions using Claude's own files only: `~/.claude/sessions/{pid}.json`, transcripts under `~/.claude/projects/`, and `history.jsonl` (for each configured config dir). Controlled sessions are observed directly through stream-json. A Nidus hook binary is added only if a concrete gap appears, and it must never launch or depend on Perch.

## Consequences

- No writes to the user's `~/.claude/settings.json` to start with.
- Some signals Perch gets from hooks (e.g. teammate idle) may be missing for observed sessions until proven necessary.
