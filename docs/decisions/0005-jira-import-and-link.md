# 0005: Jira is import + link; tasks are a links hub

- Status: accepted
- Date: 2026-11-25

## Context

Tasks often relate to several Jira issues, plus PRs, Confluence pages and docs. Two-way sync brings conflicts, field mapping and a second source of truth.

## Decision

Jira issues are imported (to create a task) or linked (to an existing task). A task can reference any number of links of any kind. Status changes are pushed to Jira only on an explicit action. Each link kind has a `LinkResolver` that gives agents a title and summary up front and full detail on demand (`ochre task fetch`).

## Consequences

- The task is the single place an agent starts from to find related information.
- Fetched content is untrusted (prompt-injection risk) and labelled as such.
- Jira state can drift from task state; the UI shows the linked issue's live status rather than copying it.
