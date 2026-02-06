# InfernalHierarchy – TODO (Pending Work Only)

> **Last Updated:** February 6, 2026  
> **Scope:** This file lists only items that are **not yet implemented or not yet completed**.  
> **Completed items moved to:** `COMPLETED.md`

---

## 📋 Table of Contents
1. [Now (Next Sprint)](#-now---next-sprint)
2. [Active Gaps / Known Limitations](#-active-gaps--known-limitations)
3. [Operational Runbooks](#-operational-runbooks)
4. [Should Have (Production Readiness)](#-should-have---production-readiness)
5. [Could Have (Future Enhancements)](#-could-have---future-enhancements)

---

## 🔴 NOW - Next Sprint

- [ ] **Operationalize vector search end-to-end**
  - Validate the `docker-compose.yml` stack (Qdrant + Host) performs end-to-end semantic memory queries.
  - Enable ONNX embeddings and verify local model assets are present and loaded correctly.
  - Run the opt-in live integration test (`INFERNAL_LIVE_QDRANT=1`) to validate Qdrant roundtrip.
  - Produce a short operator runbook section in `README.md` or `NEXT_STEPS.md` (how to run + what “good” looks like).


## 🔧 Active Gaps / Known Limitations

- [ ] **Add an opt-in analyzer gate** (StyleCop/.NET analyzers)
  - Keep build-time analyzers disabled by default, but add a dedicated CI job (or local script) that runs analyzers explicitly when desired.

- [ ] **Increase test coverage outside Telegram**
  - Prioritize: `InfernalHierarchy.Messaging` (non-ChannelMessageBus paths), `InfernalHierarchy.Agents` (Saga/CQRS), and `InfernalHierarchy.Core.CQRS`.

---

## 🧰 Operational Runbooks

- [ ] **Memory pruning runbook + defaults**
  - Define safe operational defaults (retention windows, dry-run guidance).
  - Document backup/rollback expectations before enabling pruning.

---

## 🎯 SHOULD HAVE - Production Readiness

- [ ] **Automated backup for LiteDB** - Scheduled backups + rotation strategy
- [ ] **Agent quota system** - Per-tenant/per-user agent creation quotas (global/rank caps already exist via `ResourceLimitService`)
- [ ] **Performance profiling (advanced)** - MiniProfiler/tracing viewer (built-in perf UI now includes charts + per-route HTTP latency)
  - Already implemented: perf UI charts, per-route HTTP latency, histogram stats, span summaries, basic trace capture + trace list/detail/download.
  - Remaining scope: richer trace viewer UX (timeline/waterfall, span links/search/filter) and/or MiniProfiler.

---

## 💡 COULD HAVE - Future Enhancements

### Memory & Learning
_No additional Memory & Learning backlog items currently tracked here._

### Tool Ecosystem
- [ ] **API integration tools** - GraphQL-first client + auth helpers (REST covered by `http_request`)
- [ ] **Database query tools** - SQL query execution (read-only)

### Agent Capabilities
_No additional Agent Capabilities backlog items currently tracked here._

### LLM Enhancements
- [ ] **Vision model support** - Image analysis with multi-modal models

### UI & Interfaces
_No additional UI & Interfaces backlog items currently tracked here._

### Deployment & Operations
- [ ] **Kubernetes deployment** - Helm charts/operators
- [ ] **Horizontal scaling** - Multi-host scaling strategy
- [ ] **A/B testing framework** - Compare agent behaviors
- [ ] **Blue-green deployments** - Zero-downtime deployments
- [ ] **Chaos engineering** - Resilience testing tools

### Developer Experience
- [ ] **Agent playground** - Interactive testing environment
- [ ] **Debugging tools** - Step-through agent reasoning
- [ ] **Plugin SDK** - Third-party tool development kit

