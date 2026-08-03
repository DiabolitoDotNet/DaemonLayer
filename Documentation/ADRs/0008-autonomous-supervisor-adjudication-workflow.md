# ADR 0008: Autonomous supervisor adjudication workflow for unresolved collaboration

- Status: Accepted
- Date: 2026-08-02

## Context

Collaboration and federation strategies can end with unresolved outcomes (tie, low confidence, or multi-round exhaustion).

Historically, unresolved paths emitted action labels such as `supervisor_adjudication_workflow` without guaranteed runtime execution. This leaves a non-terminal control gap and prevents a strict autonomy claim.

## Decision

Unresolved collaboration outcomes must trigger an executable autonomous adjudication workflow in runtime.

- Local collaboration (`AgentCollaborationService`) executes adjudication when unresolved conflict metadata indicates escalation.
- Cross-instance federation (`FederationService`) executes the same adjudication pattern for unresolved strategy outcomes.
- Adjudication returns a terminal decision whenever possible and does not end in action-token-only guidance.
- Tie-breaking is deterministic: higher rank wins; on equal rank/confidence, lexical `AgentId` ordering is used.

## Consequences

Positive:

- removes a non-terminal autonomy gap in unresolved conflict handling,
- provides deterministic, auditable conflict resolution,
- aligns local and federated behavior under one runtime pattern.

Trade-offs:

- adjudication policy introduces explicit decision heuristics (rank-priority and stable tie-break),
- conflict outcomes may prefer hierarchy over plurality in ambiguous cases,
- additional tests are required to keep determinism and semantics stable.

## Notes / Links

- Related code:
  - `src/InfernalHierarchy.Agents/Collaboration/AgentCollaborationService.cs`
  - `src/InfernalHierarchy.Messaging/Federation/FederationService.cs`
- Related tests:
  - `tests/InfernalHierarchy.Agents.Tests/AgentCollaborationServiceTests.cs`
  - `tests/InfernalHierarchy.Messaging.Tests/FederationServiceTests.cs`
