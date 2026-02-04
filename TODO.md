# InfernalHierarchy – TODO (Pending Work Only)

> **Last Updated:** February 4, 2026  
> **Scope:** This file lists only items that are **not yet implemented or not yet completed**.  
> **Completed items moved to:** `COMPLETED.md`

---

## 📋 Table of Contents
1. [Active Gaps / Known Limitations](#-active-gaps--known-limitations)
2. [Should Have (Production Readiness)](#-should-have---production-readiness)
3. [Could Have (Future Enhancements)](#-could-have---future-enhancements)
4. [Priority Recommendations](#-priority-recommendations)

---

## 🔧 Active Gaps / Known Limitations

1. **StyleCop warnings**
   - Warning count remains high; focus on fixing the highest-signal categories first.

2. **Vector search operational dependency**
   - Vector memory is implemented and enabled by default in configuration, but requires Qdrant to be running.
   - `/health` now includes a dedicated Qdrant health check (when vector memory is enabled).

3. **ONNX embeddings disabled by default**
   - `OnnxEmbeddingOptions.Enabled` is off by default, which may reduce semantic search quality.
   - Requires local model assets (see `models/README.md`) and explicit enablement.

4. **MemoryPruningOptions disabled by default**
   - Kept off to reduce accidental data loss; needs explicit enablement and operational guardrails.

---

## 🎯 SHOULD HAVE - Production Readiness

- [ ] **Automated backup for LiteDB** - Scheduled backups + rotation strategy
- [ ] **Rate limiting for tools** - Prevent abuse (web search / expensive tools)
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
- [ ] **Fine-tuned models** - LoRA adapters for specialized tasks

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
- [ ] **Backup automation** - Automated LiteDB backups (rotation)
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

---

## 📦 PRIORITY RECOMMENDATIONS

### 🔴 High Priority (Next Sprint)
1. **Operationalize vector search** - Run Qdrant + enable ONNX embeddings; validate semantic search end-to-end
2. **Implement rate limiting for tools** - Protect expensive tools

### 🟡 Medium Priority (Next Quarter)
1. **File system tools** - Sandboxed local file operations
2. **Web dashboard** - Hierarchy visualization and monitoring
3. **Code execution tools** - Safe sandbox execution
4. **Automated backups** - Scheduled backups + retention
5. **Semantic memory clustering** - Similarity-based grouping

### 🟢 Low Priority (Backlog)
1. **Kubernetes deployment** - Helm charts
2. **Plugin SDK** - Extensibility surface
3. **Advanced UI features** - Voice/vision/Discord
4. **Chaos engineering** - Resilience testing