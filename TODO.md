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

_No items currently tracked for this section._


## 🔧 Active Gaps / Known Limitations

- [ ] **Add an opt-in analyzer gate** (CI job or local script)
  - Analyzer configuration already exists (off-by-default in `Directory.Build.props`).
  - Add a dedicated script (and/or CI job when CI is introduced) that runs analyzers explicitly for tightening quality gates.

- [ ] **Increase test coverage outside Telegram**
  - Prioritize: `InfernalHierarchy.Messaging` (non-ChannelMessageBus paths), `InfernalHierarchy.Agents` (Saga/CQRS), and `InfernalHierarchy.Core.CQRS`.

---

## 🧰 Operational Runbooks

_No additional operational runbooks currently tracked here._

---

## 🎯 SHOULD HAVE - Production Readiness

- [ ] **Automated backup for LiteDB** - Scheduled backups + rotation strategy
- [ ] **Agent quota system** - Per-tenant/per-user agent creation quotas (global/rank caps already exist via `ResourceLimitService`)

- [ ] **Embedded UI maintainability (DRY)**
  - Remaining scope: split `DashboardAssets.cs` (CSS/JS) into per-page/per-asset modules (partial classes or embedded resources) to reduce churn and improve readability.

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
- [ ] **Blue-green deployments** - Zero-downtime deployments
- [ ] **Chaos engineering** - Resilience testing tools

- [ ] **Voice sidecar services (later)** - long-lived Whisper.cpp + Piper services
  - Note: the current **in-container/local** voice path is already implemented (whisper.cpp binary + ffmpeg + Piper.Net in-process) via `Dockerfile` + `docker-compose.voice.yml`.
  - Goal: keep STT/TTS models hot and isolate CPU/RAM usage from the Host, while still supporting the embedded UI voice endpoints.
  - Proposed containers:
    - `voice-stt` (whisper.cpp) running as a long-lived service (model loaded once). Exposes an internal HTTP endpoint like `POST /transcribe` accepting an audio payload (or a shared-volume file path) and returning `{ transcript, segments, timings }`.
    - `voice-tts` (Piper) running as a long-lived service (voice loaded once). Exposes `POST /speak` returning WAV bytes (or a shared-volume output path).
    - Optional `voice-preprocess` (ffmpeg) is usually unnecessary as a separate container; either:
      - run ffmpeg inside `voice-stt`, or
      - keep ffmpeg in the Host container and upload WAV to `voice-stt`.
  - Integration approach:
    - Add alternative tool implementations (or a mode switch) so `audio_transcribe` / `tts_speak` can call the sidecars over HTTP instead of running local processes.
    - Keep the existing local-first process-runner path as a fallback when sidecars are disabled.
  - Compose wiring (high level):
    - Mount model directories read-only: `./models/whisper:/models/whisper:ro`, `./models/piper:/models/piper:ro`.
    - Use an internal Docker network; expose no public ports for voice services.
    - Add health checks + resource limits (CPU/memory) for `voice-stt` and `voice-tts`.
    - Use environment variables in the Host to select backend: `VoiceTranscription:Backend=sidecar|local`, `TextToSpeech:Backend=sidecar|local`, and set sidecar URLs.

### Developer Experience
- [ ] **Agent playground** - Interactive testing environment
- [ ] **Debugging tools** - Step-through agent reasoning
- [ ] **Plugin SDK** - Third-party tool development kit

