# 0004: Claude Code only, behind an adapter seam

- Status: accepted
- Date: 2026-11-25

## Context

Most ADEs run many agents (Codex, Gemini, Copilot). The only subscription available for testing is Claude Code.

## Decision

Support Claude Code only. Keep launching and observing behind an `AgentAdapter` interface where that costs little, but don't build abstractions for agents that can't be tested.

## Consequences

- Faster progress on the one agent that matters now.
- Claude-specific concepts (stream-json, config dirs, transcripts) may leak into the kernel; accept that until a second agent is real, then refactor.
