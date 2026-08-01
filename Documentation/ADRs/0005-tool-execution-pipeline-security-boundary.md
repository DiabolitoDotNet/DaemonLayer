# ADR 0005: Tool execution pipeline as the security boundary

## Context

Agents do not call privileged capabilities directly. They request tool execution, and those requests must consistently flow through authorization, optional caching, rate limiting, execution, and auditing.

Without a single execution boundary, behavior drifts between hosts/tests/call paths and security-sensitive checks can become bypassable.

## Decision

The centralized tool execution pipeline is the authoritative boundary for tool execution.

- tool lookup happens in the registry,
- cross-cutting execution concerns happen in the pipeline,
- authorization decisions are evaluated against normalized tool identities,
- caching and telemetry use canonicalized tool input,
- side-effect suppression lives in the agent/tool orchestration layer, not in arbitrary tool implementations.

## Consequences

Positive:

- one place for authorization, rate limiting, and cache policy,
- less behavior drift across runtime paths,
- easier observability and auditability.

Trade-offs:

- tests and alternate hosts must wire the pipeline correctly,
- some simple tool execution paths become more explicit,
- agent-level orchestration still needs local rules for duplicate suppression and terminal side effects.