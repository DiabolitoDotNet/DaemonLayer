# ADR 0001: Local-first runtime with Telegram + Ollama

- **Date:** 2026-02-06
- **Status:** Accepted
- **Deciders:** Project maintainers
- **Tags:** local-first, interface, llm

## Context

The system is intended to run as a long-lived autonomous agent host on a single machine with strong control over data locality, performance, and operational cost.

Key constraints and goals:

- Primary human interface must be simple and ubiquitous.
- LLM inference should be runnable locally and swap-able.
- Memory should be embedded/durable without requiring external infrastructure.
- The system must remain useful without cloud dependencies.

## Decision

- Use **Telegram** as the primary interactive interface for operators.
- Use **Ollama** (OpenAI-compatible API) as the default local LLM endpoint.
- Keep the core runtime **local-first** (embedded persistence, optional local services like SearXNG).

## Alternatives considered

- **Web UI first**
  - Pros: richer interaction, visualizations.
  - Cons: higher build/maintenance cost; more moving parts; less “always available” than Telegram.

- **Cloud LLM providers by default**
  - Pros: easier initial setup, strong models.
  - Cons: cost, privacy, network dependency, operational complexity.

- **External database (Postgres/SQL Server) by default**
  - Pros: familiar operational model, scalability.
  - Cons: violates local-first bias; additional infrastructure.

## Consequences

### Positive

- Low-friction interaction channel (Telegram) with minimal UI work.
- Predictable cost and improved privacy via local inference.
- Simplified deployment: a single host process + embedded storage.

### Negative / Trade-offs

- Telegram introduces an external dependency for interactive use.
- Local inference requires a machine capable of running the chosen model(s).
- Some advanced UI workflows (dashboards, rich review) require extra work beyond Telegram.

## Notes / Links

- Related docs: ../Solution-Architecture.md, ../../README.md
