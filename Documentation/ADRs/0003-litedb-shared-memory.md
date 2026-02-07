# ADR 0003: LiteDB-backed shared memory with pruning/retention

- **Date:** 2026-02-06
- **Status:** Accepted
- **Deciders:** Project maintainers
- **Tags:** memory, persistence, litedb, retention

## Context

Agents require durable, queryable memory across runs (facts, decisions, tasks). The solution is local-first and should not require external infrastructure.

Key forces:

- Embedded persistence preferred (simple deployment, fewer moving parts).
- Structured records and basic querying are required.
- Storage must remain bounded over time; retention rules must exist.
- Must remain testable and replaceable (avoid hard dependency on a single engine).

## Decision

Use LiteDB as the primary embedded storage engine and expose it through the `ISharedMemory` abstraction.

- Persist durable records (facts, decisions, tasks).
- Provide visibility controls (private/shared/rank-based/public) at the record level.
- Run background pruning/retention enforcement to prevent unbounded growth.
- Keep vector memory (`IVectorMemory`) optional and additive (semantic retrieval when configured).

## Alternatives considered

- **SQLite**
  - Pros: ubiquitous, durable.
  - Cons: additional schema/migrations; more boilerplate than a document store for rapidly evolving records.

- **External database (Postgres/SQL Server)**
  - Pros: operational familiarity, scale.
  - Cons: violates local-first bias; adds infrastructure.

- **File-based JSON logs**
  - Pros: trivial.
  - Cons: poor querying; hard concurrency; grows unbounded; hard to evolve.

## Consequences

### Positive

- Single-process deployment with durable storage.
- Simple evolution of record shapes (document-like).
- Clear abstraction boundary (`ISharedMemory`) for testing and future backends.

### Negative / Trade-offs

- Concurrency patterns must be carefully managed (single-file DB characteristics).
- Query capabilities are more limited than full RDBMS.
- Retention/pruning is required for long-lived hosts.

## Notes / Links

- Related code: `InfernalHierarchy.Core.Interfaces.ISharedMemory`, `InfernalHierarchy.Memory`
- Related docs: ../Solution-Architecture.md
