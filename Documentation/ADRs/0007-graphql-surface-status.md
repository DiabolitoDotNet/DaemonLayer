# ADR 0007: GraphQL Surface Status

- Status: Accepted
- Date: 2026-08-01

## Context

The repository contains a GraphQL project source tree, but the current host composition root and solution workflow focus on HTTP, WebSocket, Telegram, and tool-based surfaces. The active TODO requires deciding GraphQL status and aligning supported surface documentation.

## Decision

GraphQL is classified as archived/experimental and is not part of the supported runtime surface for P1 production readiness.

- It remains in source control for future reactivation.
- It is excluded from the current solution build/test/release gate.
- Production readiness commitments do not include GraphQL endpoints.

## Consequences

- Supported surfaces are now explicitly: REST API, WebSocket, Telegram, Voice (when enabled), tool interfaces.
- Any future GraphQL activation requires a new ADR, host wiring, auth/security parity, and full test coverage before being marked supported.
