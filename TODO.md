# InfernalHierarchy – TODO (Pending Work Only)

> **Last Updated:** February 13, 2026  
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

- [ ] **Runbook: “Custom tools” end-to-end (create → policy → compile → register → invoke)**
  - Documenter le pipeline complet:
    - Meta-tool `create_custom_tool` (génération source C# → policy → compilation Roslyn → persistence LiteDB → registration `ToolRegistry`).
    - Outil debug `custom_tool_get_source` (récupération tool_id, source_hash, code stocké, statut compile/policy).
    - Overwrite/update: comment une régénération remplace bien l’implémentation d’un `custom_*` déjà existant.
  - Inclure 2 recettes reproductibles:
    - “Créer un HTTP GET JSON tool” (base_url + endpoint) + invocation qui renvoie du JSON réel.
    - “Diagnostiquer un tool qui ne se met pas à jour” (hash, logs “Updated tool”, store vs registry).
  - Capturer les pièges déjà rencontrés:
    - /api/chat: schéma & binding (keys `Message`, `ToAgentId`, `TimeoutMs` en PascalCase).
    - Forced invocation: format attendu “Invoke tool <name> {json}”.

- [ ] **Runbook: Tool authorization debugging (incl. custom tools “Supreme-only”)**
  - Expliquer clairement:
    - Pourquoi `custom_*` est Supreme-only par défaut.
    - Où et comment configurer `ToolPermissions` pour autoriser création/invocation à d’autres agents.
    - Comment interpréter les deny reasons (policy vs permissions vs allowlist persona).
  - Ajouter une checklist de triage (persona allowlist → ToolPermissions → policy → compilation → registry).

- [ ] **Permissions: délégation contrôlée de création de tools à certains agents (opt-in)**
  - Objectif: permettre à un Prince/Duke spécifique (ex: Asmodeus, Baal, Vassago) de créer des custom tools sans ouvrir globalement.
  - Livrables:
    - Paramétrage clair (config) + exemples.
    - Tests couvrant: agent autorisé vs non autorisé, et scope (create_custom_tool vs invocation du tool créé).
    - Logs structurés: “permission granted/denied” avec agent_id, tool_name, reason.

- [ ] **Durcir la policy custom tools (réduire les faux positifs + précision)**
  - Aujourd’hui: scan regex sur code “comment-stripped”.
  - À faire:
    - Ignorer aussi les *string literals* (ex: texte contenant “file”) ou basculer sur parsing Roslyn (tokens/syntax tree).
    - Remplacer/compléter la règle “File/Directory APIs” par une détection plus sémantique (ex: `System.IO.*` + symboles connus) au lieu d’un simple mot-clé.
    - Ajouter des tests de non-régression: “File” en string/comment ne doit pas bloquer; `System.IO.File` doit bloquer.

- [ ] **Stabiliser la DX: nettoyer les warnings de build qui masquent les régressions**
  - Traiter au minimum les warnings récurrents observés au build Docker:
    - CS8619 (nullability mismatch) dans `DefaultToolExecutionPipeline`.
    - CA2024 (EndOfStream en async) dans `OllamaClient`.
    - CS0162 (unreachable code) dans `AgentFactory`.
  - But: rendre les logs utiles et préparer un “analyzer gate”.


## 🔧 Active Gaps / Known Limitations

- [ ] **Add an opt-in analyzer gate** (CI job or local script)
  - Analyzer configuration already exists (off-by-default in `Directory.Build.props`).
  - Add a dedicated script (and/or CI job when CI is introduced) that runs analyzers explicitly for tightening quality gates.

- [ ] **Clarifier et tester les comportements “stop-after-success / dedupe” pour toutes les tools à effets de bord**
  - Déjà traité pour l’email (signature canonique + arrêt après succès).
  - Reste à cadrer:
    - Définir la liste des tools side-effect (réseau, notifications, écritures mémoire, filesystem/process) + stratégie dedupe.
    - Ajouter quelques tests ciblés sur 1–2 tools réseau supplémentaires (même mécanique de canonicalization).

- [ ] **Increase test coverage outside Telegram**
  - Prioritize: `InfernalHierarchy.Messaging` (non-ChannelMessageBus paths), `InfernalHierarchy.Agents` (Saga/CQRS), and `InfernalHierarchy.Core.CQRS`.

---

## 🧰 Operational Runbooks

_No additional operational runbooks currently tracked here._

- [ ] **Runbook: End-to-end request tracing (Telegram → Agent → Tool → Memory → Telegram)**
  - Include: where to look in logs/traces/metrics, correlation identifiers, and common failure modes.

- [ ] **Runbook: Tool authorization debugging**
  - Include: how to interpret deny reasons, where tool-permission config lives, and how to reload permissions safely.

---

## 🎯 SHOULD HAVE - Production Readiness

- [ ] **Automated backup for LiteDB** - Scheduled backups + rotation strategy
- [ ] **Agent quota system** - Per-tenant/per-user agent creation quotas (global/rank caps already exist via `ResourceLimitService`)

- [ ] **Embedded UI maintainability (DRY)**
  - Remaining scope: split `DashboardAssets.cs` (CSS/JS) into per-page/per-asset modules (partial classes or embedded resources) to reduce churn and improve readability.

- [ ] **Architecture diagrams (C4 + sequence)**
  - Add Mermaid diagrams for: container/component architecture, and the typical runtime sequence (Telegram update → validation → orchestrator → ReAct → tool pipeline → memory → response).

- [ ] **Documenter /api/chat et la sémantique “forced invocation”**
  - Ajouter une section courte mais précise dans la doc (ou un runbook):
    - Schéma de requête/réponse.
    - Casing des champs.
    - Exemples PowerShell (construction JSON sûre) pour éviter les erreurs d’échappement.

---

## 💡 COULD HAVE - Future Enhancements

### Memory & Learning
_No additional Memory & Learning backlog items currently tracked here._

### Tool Ecosystem
- [ ] **API integration tools** - GraphQL-first client + auth helpers (REST covered by `http_request`)
- [ ] **Database query tools** - SQL query execution (read-only)

- [ ] **Meta tools complémentaires pour custom tools**
  - `custom_tool_list` (liste: tool_name, tool_id, last_compiled_at, requires_manual_approval).
  - `custom_tool_delete` (supprimer un tool + retirer du registry) avec garde-fous.
  - Objectif: opérer les tools sans accéder à LiteDB à la main.

### Agent Capabilities
_No additional Agent Capabilities backlog items currently tracked here._

### LLM Enhancements
- [ ] **Vision model support** - Image analysis with multi-modal models

### UI & Interfaces
_No additional UI & Interfaces backlog items currently tracked here._

- [ ] **Improve French TTS voice quality (accent/language)**
  - Current TTS output reads French with an obvious US accent.
  - Goal: select a French-capable voice/model (or swap TTS backend) so FR replies sound natural.

### Deployment & Operations
- [ ] **Kubernetes deployment** - Helm charts/operators
- [ ] **Horizontal scaling** - Multi-host scaling strategy
- [ ] **Blue-green deployments** - Zero-downtime deployments
- [ ] **Chaos engineering** - Resilience testing tools

- [ ] **Voice sidecar services (later)** - long-lived STT + TTS services
  - Note: the current **in-container/local** voice path is already implemented (Faster-Whisper + Kokoro-82M via Python helpers) via the `runtime-voice` target in `Dockerfile` + `docker-compose.voice.yml`.
  - Goal: keep STT/TTS models hot and isolate CPU/RAM usage from the Host, while still supporting the embedded UI voice endpoints.
  - Proposed containers:
    - `voice-stt` (Faster-Whisper) running as a long-lived service (model loaded once). Exposes an internal HTTP endpoint like `POST /transcribe` accepting an audio payload (or a shared-volume file path) and returning `{ transcript, segments, timings }`.
    - `voice-tts` (Kokoro) running as a long-lived service (voice loaded once). Exposes `POST /speak` returning WAV bytes (or a shared-volume output path).
    - Optional `voice-preprocess` (ffmpeg) is usually unnecessary as a separate container; either:
      - run ffmpeg inside `voice-stt`, or
      - keep ffmpeg in the Host container and upload WAV to `voice-stt`.
  - Integration approach:
    - Add alternative tool implementations (or a mode switch) so `audio_transcribe` / `tts_speak` can call the sidecars over HTTP instead of running local processes.
    - Keep the existing local-first process-runner path as a fallback when sidecars are disabled.
  - Compose wiring (high level):
    - Mount the Hugging Face cache: `./models/hf:/models/hf` and set `HF_HOME=/models/hf`.
    - Use an internal Docker network; expose no public ports for voice services.
    - Add health checks + resource limits (CPU/memory) for `voice-stt` and `voice-tts`.
    - Use environment variables in the Host to select backend: `VoiceTranscription:Backend=sidecar|local`, `TextToSpeech:Backend=sidecar|local`, and set sidecar URLs.

### Developer Experience
- [ ] **Agent playground** - Interactive testing environment
- [ ] **Debugging tools** - Step-through agent reasoning
- [ ] **Plugin SDK** - Third-party tool development kit

- [ ] **Architecture Decision Records (ADRs)**
  - Capture key decisions (Channels message bus, LiteDB shared memory, tool pipeline security boundaries, local-first/Ollama constraint, Telegram as primary interface).

- [ ] **Documentation hardening: Capabilities “recipes” + extension guide**
  - Make `Documentation/Capabilities.md` more actionable with concrete workflows, guardrails, and how-to extend (new tool, new persona, new agent type).

- [ ] **ADRs: tool pipeline security boundary + persona/template model**
  - Add ADRs covering: (1) centralized tool execution pipeline as the security boundary, (2) personas/templates as JSON assets under `souls/` and `templates/`.

