# 0003: Separate repo; copy code from Perch and own it

- Status: accepted
- Date: 2026-11-25

## Context

Nidus is a radical experiment. Building it inside Perch would bloat an always-on tray app and couple the experiment to Perch's release cycle. Sharing code by reference (submodule, package) adds friction while both are changing fast.

## Decision

Nidus lives in its own repo. Code from Perch and the user's other tools (sprig, Hypertree, flamingle) is copied in where useful and owned here. Each copy's commit message names the source repo, path and commit.

## Consequences

- Freedom to change copied code without breaking Perch.
- Fixes don't flow automatically; compare against the recorded source commit when a Perch fix lands.
- If the experiment fails, Perch is untouched.
- See [reuse-from-perch.md](../reuse-from-perch.md) for candidates.
