# InfernalHierarchy – TODO (Pending Work Only)

> **Last Updated:** February 5, 2026  
> **Scope:** This file lists only items that are **not yet implemented or not yet completed**.  
> **Completed items moved to:** `COMPLETED.md`

---

## 📋 Table of Contents
1. [Now (Next Sprint)](#-now---next-sprint)
2. [Active Gaps / Known Limitations](#-active-gaps--known-limitations)
3. [Operational Readiness](#-operational-readiness)
4. [Should Have (Production Readiness)](#-should-have---production-readiness)
5. [Could Have (Future Enhancements)](#-could-have---future-enhancements)

---

## 🔴 NOW - Next Sprint

- [ ] **Operationalize vector search end-to-end**
  - Run Qdrant locally, enable ONNX embeddings, and validate semantic memory queries end-to-end.
  - Use `/health/ready` to confirm Qdrant + embedding assets readiness.
  - Run the live integration test (opt-in) to validate Qdrant roundtrip.


## 🔧 Active Gaps / Known Limitations

1. **Analyzer signal strategy (StyleCop/.NET analyzers)**
    - Builds are currently warning-clean by running analyzers primarily in the IDE (build-time analyzers disabled).
    - Recommendation: add a dedicated CI step (or optional local script) that runs analyzers explicitly when you want to tighten quality gates.

2. **Test coverage gaps outside Telegram**
   - Telegram coverage is now strong, but several areas remain low/zero.
   - Prioritize: `InfernalHierarchy.Messaging` (non-ChannelMessageBus paths), `InfernalHierarchy.Agents` (Saga/CQRS), and `InfernalHierarchy.Core.CQRS`.

---

## 🧰 Operational Readiness

- [ ] **Vector search runtime dependency (Qdrant)**
  - Vector memory requires Qdrant to be running.
  - `/health` includes a dedicated Qdrant health check when vector memory is enabled.
  - `/health/ready` is available for readiness gating.
- [ ] **ONNX embeddings (local model assets)**
  - `OnnxEmbeddingOptions.Enabled` is off by default.
  - Enabling improves semantic search quality but requires local model assets (see `models/README.md`).
  - `/health` exposes an `onnx_embeddings` check that reports missing assets when enabled.
- [ ] **Memory pruning (guardrails required)**
  - `MemoryPruningOptions` is off by default to reduce accidental data loss.
  - Needs explicit enablement + retention/rollback considerations.

---

## 🎯 SHOULD HAVE - Production Readiness

- [ ] **Automated backup for LiteDB** - Scheduled backups + rotation strategy
- [ ] **Agent quota system** - Limit agent creation per user/time window
- [ ] **Performance profiling UI** - MiniProfiler or similar (basic `PerformanceMonitor` already exists)

---

## 💡 COULD HAVE - Future Enhancements

### Memory & Learning
_No additional Memory & Learning backlog items currently tracked here._

### Tool Ecosystem
- [ ] **File system tools** - Read/write/search local files (sandboxed)
- [ ] **Code execution tools** - Sandboxed Python/Node.js execution
- [ ] **API integration tools** - Generic REST/GraphQL client
- [ ] **Database query tools** - SQL query execution (read-only)
- [ ] **Notification tools** - Email/Slack/Discord integrations
- [ ] **Image generation tools** - Local Stable Diffusion or similar
- [ ] **Audio transcription** - Whisper.cpp integration
- [ ] **Tool marketplace** - Hot-load tools from external assemblies

### Agent Capabilities
- [ ] **Agent migration** - Move agents between hosts

### LLM Enhancements
- [ ] **Vision model support** - Image analysis with multi-modal models

### UI & Interfaces
- [ ] **Web dashboard** - Blazor/React admin panel
- [ ] **CLI client** - Local command-line interface
- [ ] **REST API** - HTTP API for external integrations
- [ ] **WebSocket support** - Real-time updates
- [ ] **Discord bot** - Alternative to Telegram
- [ ] **Voice interface** - Speech-to-text + text-to-speech

### Deployment & Operations
- [ ] **Kubernetes deployment** - Helm charts/operators
- [ ] **Horizontal scaling** - Multi-host scaling strategy
- [ ] **A/B testing framework** - Compare agent behaviors
- [ ] **Blue-green deployments** - Zero-downtime deployments
- [ ] **Chaos engineering** - Resilience testing tools

### Developer Experience
- [ ] **Agent playground** - Interactive testing environment
- [ ] **Persona editor** - Visual JSON editor for souls
- [ ] **Debugging tools** - Step-through agent reasoning
- [ ] **Performance profiler** - Identify bottlenecks
- [ ] **Plugin SDK** - Third-party tool development kit
- [ ] **Documentation generator** - Auto-generate docs from code
