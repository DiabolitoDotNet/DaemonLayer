# InfernalHierarchy – Completed Work Log

> **Last Updated:** August 3, 2026
>
> This file contains items/features that are **implemented / completed** and were moved out of `TODO.md` to keep the TODO list focused on remaining work.
>
> For deeper technical details, see:
> - `IMPLEMENTATION_SUMMARY.md`
> - `ADVANCED_FEATURES.md`
> - `OBSERVABILITY.md` / `OBSERVABILITY_SUMMARY.md`
> - `SECURITY_CONFIG.md`

---

## ✅ Implemented Capabilities (High Level)

### Code Quality & Tooling ✅
- **Directory.Build.props**: project-wide analyzer configuration
  - StyleCop.Analyzers v1.2.0-beta.556
  - AnalysisMode=All
  - EnforceCodeStyleInBuild=false (IDE/live analysis preferred; build warnings kept clean by default)
  - RunAnalyzersDuringBuild=true (strict parity with CI by default)
  - Nullable reference types enabled
  - XML documentation generation enforced
- **.editorconfig**: comprehensive style guide
  - Naming conventions, formatting rules, async suffix enforcement, etc.

### Advanced Memory Features ✅
- **VectorMemoryService**: semantic search with Qdrant
  - 384-dimensional embeddings (cosine similarity)
  - Docker container ports 6333 (REST) / 6334 (gRPC)
  - `StoreFactWithVectorAsync`, `SearchSimilarAsync`
  - `InitializeCollectionAsync` auto-create
- **Vector search operationalization**
  - ONNX tokenizer loading supported (`tokenizer.json` / `vocab.txt`) and logged on startup
  - `/health/ready` embeddings check reports loaded vs fallback (`data.using_fallback`)
  - Operator-only smoke endpoint for deterministic index→search validation: `POST /api/ops/vector/smoke` (gated by `OperatorApi` key)
  - Live Qdrant test can validate real ONNX embeddings when assets exist (via `INFERNAL_ONNX_*` env vars)
- **Vector memory abstraction**
  - `IVectorMemory` interface for agents/tools (decouples from Qdrant implementation)
  - Centralized visibility logic via `MemoryVisibilityRules`
- **MemoryPruningService**: BackgroundService cleanup
  - Configurable interval (`PruningIntervalHours`)
  - Prunes low-confidence facts
  - Archives decisions to `./archive/memory`
  - Removes completed tasks beyond retention
- **Operational runbook (memory pruning)**
  - Added safe defaults (dry-run + per-run delete cap) and documented backup/rollback procedure
- **Memory Versioning**
  - `Fact.Version`, `Fact.PreviousVersionId`, `Fact.IsArchived`
  - `UpdateFactAsync` creates an immutable history chain
  - `SoftDeleteFactAsync` archives instead of removing
- **Delete Operations**
  - `DeleteDecisionAsync`, `DeleteFactAsync`, `DeleteTaskAsync`, `SoftDeleteFactAsync`

### LLM Enhancements ✅
- **MultiModelLlmClient**: dynamic model selection
  - 4 complexity levels (Simple/Medium/Complex/Expert)
  - Automatic fallback chain
  - Per-model configuration (name/temperature/max tokens)
  - Result-based APIs for expected failures (`TryGetCompletionAsync`, `TryGetStreamingCompletionAsync`)
- **Streaming Responses**
  - `GetStreamingCompletionAsync` returning `IAsyncEnumerable<string>`
- **TokenUsageTracker**
  - Per-model + per-agent usage metrics
  - Rolling history and stats (tokens/sec, durations, estimated cost)
- **AgentLearningService**
  - Tool performance tracking + recommendations
  - Thread-safe (ConcurrentDictionary)
- **Structured outputs (ReAct JSON mode)**
  - JSON-first ReAct responses (tool invocation + FINAL_ANSWER), with legacy text parsing fallback
  - Configurable via `ReActOptions:UseJsonResponse` (enabled by default)
- **ReAct SRP components wired via DI (phase 1)**
  - `ReActAgent` can now be composed with injected `IActionParser` / `IActionExecutor` / `IReActPromptBuilder` / `IReActLoopRunner` / reporting services (defaults preserved)
- **RAG integration**
  - ReAct agents can inject retrieved, visibility-filtered facts into context
  - Uses vector search when available (Qdrant), with safe fallback to LiteDB visibility-aware keyword search
- **Memory learning (disabled by default)**
  - Background service for semantic clustering and LLM-based fact compression/summarization
- **Prompt optimization (A/B testing for system prompts)**
  - `prompt_ab_test` tool for running repeatable trials across prompt variants and producing a JSON report + winner
- **Fine-tuned model selection (persona-level overrides)**
  - Personas can optionally set `ModelOverride` in their soul JSON to use a fine-tuned Ollama model name per agent
  - Implemented via `IModelOverrideLlmClient` (optional capability) in the Ollama client

### Agent Collaboration ✅
- **Complete Agent Collaboration System**
  - Collaboration requests now flow end-to-end over the message bus (agents process `MessageType.CollaborationRequest`)
  - Added end-to-end multi-agent collaboration tests (real `ChannelMessageBus` + `AgentCollaborationService`)
- **Aggregation strategies extracted**
  - `AgentCollaborationService` aggregation logic decomposed behind `IAggregationStrategy` for independent testability
- **Signal-based response waiting (no polling)**
  - Replaced polling/wait loops with Channel-based signaling and round-aware response handling to reduce CPU wakeups and avoid cross-round mixing

### External Integrations ✅
- **Typed HTTP clients for web search**
  - Introduced typed provider clients (SearXNG + Brave Search) with consistent parsing and error mapping

### Tool Ecosystem ✅
- **Sandboxed filesystem tools**
  - `fs_read`, `fs_write`, `fs_search` with sandbox root + extension allowlist + size limits
- **Allowlisted HTTP tool**
  - `http_request` with scheme/host/method allowlists, timeouts, and max response size
- **Constrained code execution tools**
  - `python_exec`, `node_exec` with sandboxed working directory + timeouts + output limits
- **Authorization enforced at execution-time**
  - Tool permissions are checked inside the tool execution pipeline (single choke point), with agent name propagated for allow/deny rules
- **Tool marketplace (hot-load tools)**
  - `ToolMarketplaceHostedService` loads allowlisted plugin DLLs from a directory and registers discovered `ITool` implementations at runtime

### Observability & Monitoring ✅
- **DistributedTracing**: OpenTelemetry Activity-based tracing
- **HTTP Health Endpoint**: `/health` JSON endpoint (gated by `Http.Enabled`)
- **Prometheus Metrics Endpoint**: `/metrics` text exposition (gated by `Http.Enabled`)
- **OTLP Exporter Wiring**: configurable OTLP tracing exporter (enable via `OpenTelemetry:Exporters:Otlp`)
- **MetricsService**: counters/gauges/histograms
- **PerformanceMonitor**: CPU/memory/GC/threads/handles snapshots
- **In-memory trace capture (ActivityListener)**
  - APIs: `GET /api/perf/traces`, `GET /api/perf/traces/{traceId}`, `GET /api/perf/traces/{traceId}/download`
- **Embedded perf UI trace viewer upgrades**
  - Added span selection, trace summary, and a simple waterfall visualization for relative span timing
  - Added trace list filter/sort, auto-refresh, and server-side trace management endpoints
    - `GET /api/perf/trace-capture` (capture status + store stats)
    - `POST /api/perf/traces/clear` (clear captured traces)
- **In-memory request profiling (MiniProfiler-like)**
  - Middleware emits `X-Request-Profile-Id` and stores recent request timings for `/ui/perf`
  - APIs: `GET /api/perf/request-profiling`, `GET /api/perf/requests`, `GET /api/perf/requests/{id}`, `POST /api/perf/requests/clear`
- **Health Checks**: Ollama, Telegram, LiteDB, Agent hierarchy, Qdrant (vector memory)
- **Structured Logging**: Serilog enrichers and sinks

### Security & Reliability ✅
- **InputValidator**: SQLi/XSS/command-injection patterns + sanitization
- **ResiliencePolicies**: Polly retry/circuit-breaker policies
- **ResourceLimitService**: per-rank limits, concurrency, timeouts
- **Tool rate limiting**: fixed-window limiter in tool pipeline (per-rank defaults + per-tool overrides)
- **Agent Suspension/Hibernation**: Suspend/Resume lifecycle states
- **SecretRotationService**: hot-reload secrets via `IOptionsMonitor<T>`
- **ToolAuthorizationService**: rank-based tool permissions + allow/deny lists

### UI & Interfaces ✅
- **Web dashboard**: built-in local dashboard served from `/ui` (no external frontend dependencies)
- **WebSocket support**: `/ws` endpoint for real-time agent/broadcast streaming + task submission
- **Voice interface (local-first)**
  - Voice API endpoints (config-gated): `POST /api/voice/transcribe` and `POST /api/voice/speak`
  - Voice tools: `audio_transcribe` (STT) + `tts_speak` (TTS), both disabled-by-default until configured
  - Dashboard Voice panel to upload audio + play TTS output when enabled
- **Performance profiling UI (built-in)**
  - Dedicated page: `/ui/perf`
  - APIs: `GET /api/perf/snapshot`, `GET /api/perf/histograms`, `GET /api/perf/http`, `GET /api/perf/spans`, `GET /api/perf/traces*`
  - Advanced view: charts + per-route HTTP latency + span summaries + basic trace viewer (list/detail/download)
- **Agent migration UI (built-in)**
  - Dedicated page: `/ui/migrate`
  - APIs: `GET /api/agents/{agentId}/export`, `POST /api/agents/import`
  - Bundle: persona JSON + memory slice (facts/tasks/decisions) with optional signature + replay guard
- **Persona editor (built-in)**
  - Dedicated page: `/ui/personas`
  - File-backed APIs: `GET /api/personas`, `GET /api/personas/{name}`, `PUT /api/personas/{name}`, `POST /api/personas/{name}/validate`
  - Persona hot-reload: `JsonPersonaLoader` refreshes cached personas when the soul JSON file changes
- **Documentation generator (built-in)**
  - Dedicated page: `/ui/docs`
  - APIs: `GET /api/docs/markdown`, `GET /api/docs/json`

### Event Sourcing ✅
- **EventStore**: append-only JSONL audit trail per agent
  - Event metadata (timestamp/type/agentId/metadata)
  - Replay and time-range querying
- **Agent/task/tool event emission**
  - Agents and tools now emit lifecycle events (agent created/terminated, task received/started/completed/failed, decision made, tool executed/errors)
  - Uses `IAgentEventSink` to keep auditing best-effort and testable

### Configuration Management ✅
- **Options validation via `IValidateOptions<T>` + `ValidateOnStart`**
  - Validation moved into dedicated validators; `Program.cs` kept composition-only
- **ConfigurationReloadService**: live reload for non-sensitive settings

### Result Model ✅
- **Core `Result<T>`**
  - Shared success/failure type introduced in Core for predictable, boundary-safe error handling

---

## ✅ Completed Limitations / TODOs
- VectorMemoryService mock embeddings → **completed** (ONNX embedding service integrated)
- Missing delete methods in `ISharedMemory` → **completed**
- Dedicated Qdrant/vector-memory health check → **completed**
- EventStore integrated into ReActAgent + tool execution pipeline → **completed**
- Metrics export to Prometheus → **completed** (`/metrics` endpoint)
- RAG integration (retrieval-augmented generation) → **completed**
- Semantic memory clustering → **completed**
- Memory compression / summarization → **completed**
- Telegram commands for token usage stats → **completed** (`/usage`, `/models`)
- Lucifer handlers for `/usage` and `/models` → **completed**
- Telegram update handling coverage uplift → **completed** (made handler internal + `InternalsVisibleTo`, expanded command-path tests; `TelegramBotService` now high coverage)
- AgentLearningService integration into tool execution pipeline → **completed**
- Function calling with structured outputs → **completed** (JSON-first ReAct responses behind `ReActOptions`)
- Prompt optimization (A/B testing for system prompts) → **completed** (`prompt_ab_test` tool + tests)
- Complete Agent Collaboration System → **completed** (CollaborationRequest end-to-end handling + E2E tests)
- Solution-wide test pass in Release configuration → **completed** (0 failures; integration-only tests may be skipped)

## ✅ Repo Hygiene
- Generated coverage reports and build/test logs ignored → **completed** (`.gitignore` updated to exclude `artifacts/`, `coverage-report/`, `TestResults/`, and common log/coverage files)

## ✅ SOLID/DRY Refactors (Feb 6, 2026)
- Host composition root simplified: extracted Minimal API mappings out of `Program.cs` into dedicated modules (UI, voice, perf, agents, personas, docs, chat, tools, events, metrics), keeping routes/behavior identical.
- Centralized repeated guards/normalization: added `LoopbackGuard`/`LocalOnlyGuard` for LocalOnly gating and `MetricKeyNormalizer` for low-cardinality route metric keys.
- ReActAgent SRP refactor (SOLID): moved command/collaboration/decision/event/RAG orchestration behind `IReAct*` services so `ReActAgent` remains a thin orchestrator.
  - Introduced `IReActTaskProcessor`, `IRagContextEnricher`, and `IAgentEventAppender` (DI-registered), preserving behavior via defaults.
  - Validated by full solution test run (`dotnet test` in Release): 479/479 passing.

---

## ✅ Reliability + Custom Tools Hardening (Feb 13, 2026)

### Side-effects & Non-determinism ✅
- Outbound email “spam” fixed: deduped send signatures (ignore timestamp/alias variance) + hard stop after first successful send; regression tests added and validated.

### Web Search Stability ✅
- SearXNG Startpage JSON decode errors stopped by disabling Startpage engines in `searxng/settings.yml`.

### Custom Tools: approval, debug, and deterministic execution ✅
- Manual approval was initially relaxed for **network-only** custom tools via config (`CustomTools:AllowNetworkWithoutManualApproval=true`) while keeping manual approval for IO/process/reflection patterns. (Superseded by A102.2 autonomous custom-tool lane.)
- Deterministic “forced invocation” fast-path in the ReAct loop: explicit `Invoke tool <name> {json}` bypasses the model and executes immediately (records tool calls reliably via `/api/chat`).
- Forced invocation now supports `create_custom_tool` as well, enabling deterministic overwrite/regeneration without relying on model choice.
- Tool registry supports replacing existing `custom_*` tools at runtime (updates existing entry instead of refusing duplicates).
- Added meta debug tool `custom_tool_get_source` to fetch persisted custom tool definition + source code from LiteDB for diagnostics.

### Custom Tool Template Fixes ✅
- Fixed URL combining in generated HTTP GET JSON tool template so endpoints like `/get` no longer resolve to `file:///get` (only treats endpoint as absolute when scheme is HTTP/HTTPS).

### Custom Tool Security Policy Robustness ✅
- Security policy false-positive fixed: policy scanning strips C# comments before regex evaluation so comment text (e.g., the word “file”) doesn’t trigger File/Directory rules; regression test added.

### End-to-End Proof ✅
- Verified: `custom_lacale_api` can be deterministically overwritten and invoked via `/api/chat` and returns real JSON from `https://httpbin.org/get`.

---

## ✅ Test Coverage (Moved from TODO.md)

## ✅ Autonomy 100% Closure (Aug 2, 2026)

- **A100.2 cross-instance collaboration closure**: `FederationService.RequestCrossInstanceCollaborationAsync` now collects and parses remote responses, aggregates decision/confidence/agreement, and preserves source-instance provenance in reasoning.
- **A100.3 saga compensation autonomy closure**: `SagaBase` now applies bounded compensation retries and returns structured failure metadata (`FailureReasonCode`, `NextAction`, `NeedsSupervisorIntervention`) with escalation hints when retries are exhausted.
- **Validation**: targeted messaging/agents test suites passing + full solution regression green (`dotnet test InfernalHierarchy.sln`, `EXIT:0`).

## ✅ Autonomy Strict-Hardening Closure (A100R, Aug 2, 2026)

- **A100R.1 federation heartbeat truthfulness**: `FederationService.MonitorInstanceHealthAsync` now marks an instance healthy only on confirmed heartbeat response; transport/status failures no longer refresh `LastHeartbeat` and set `IsActive=false`.
- **A100R.2 runtime authorization alignment**: Build/Deploy-critical tools are now enabled by default in `ToolAuthorizationService` permissions, and profile/permission drift diagnostics are logged at startup and reload.
- **A100R.3 strategy-consistent cross-instance aggregation**: federation collaboration now applies strategy-specific aggregation semantics (Voting, WeightedVoting, Consensus, HighestConfidence, Hierarchical) with deterministic fallback when participants are insufficient.
- **A100R.4 autonomous unresolved-conflict closure**: unresolved collaboration outcomes now route to `supervisor_adjudication_workflow` instead of manual-only guidance.
- **A100R.5 modern C# optimization pass**: reduced hot-path allocations by removing per-call allowlist set materialization in profile command checks while preserving policy behavior.
- **Validation**: messaging tests pass after aggregation semantics update (30/30), authorization targeted tests pass (9/9), and full solution regression remains green (`EXIT:0`).

## ✅ Autonomy Release-Gate Closure (A100F, Aug 2, 2026)

- **A100F.1 closed-loop collaboration learning**: `AgentCollaborationService` now records strategy outcomes (success/confidence/agreement/latency/rounds/participants) in `AgentLearningService` and consults learned strategy scores before static heuristics.
- **A100F.2 quantified performance gate**: added executable harness project `tools/InfernalHierarchy.PerfGate` with versioned budgets (`perf-baseline.json`) measuring latency/op and allocations/op for authorization and federation aggregation hot paths.
- **A100F.3 autonomy scorecard release gate**: added `AutonomyScorecardGateTests` and CI full-lane gate step to enforce scorecard threshold behavior as an explicit merge/release control.
- **A100F.4 federated chaos matrix + routing safety**: added deterministic federation tests for weighted-tie and low-confidence escalations, plus delegation fallback across ordered candidates when the lowest-load instance fails.

## ✅ Autonomy Certification Hardening (A500/A510/A520, Aug 3, 2026)

- **A500.1**: production startup readiness blocking enforced (`FailStartupOnCriticalNotReady=true`).
- **A500.2**: versioned critical capability matrix with explicit config dependencies and readiness evidence endpoint payload coverage.
- **A500.3**: strict certification profile added (`appsettings.AutonomyCertification.json`) with 1.0/0.0/1.0 autonomy SLO gates and higher sample floors.
- **A500.4 / A510.3**: terminal autonomy outcomes structured and contract-tested (`autonomy_outcome_*` metadata).
- **A510.1**: long-run soak scenario added to PerfGate with drift-envelope checks (completion, terminal-failure, median time-to-terminal).
- **A510.2**: representative-host perf scenarios expanded (readiness scale, scorecard volume, concurrent remediation).
- **A520.1**: analyzer suppression inventory added and linked in policy/index docs.
- **A520.2**: environment profile split documented for runtime-ops vs certification mode.
- **Validation**: targeted host gate tests (2/2), targeted federation tests (20/20), full solution regression green (`EXIT:0`).

## ✅ Strict Final Autonomy Closure (A100X, Aug 2, 2026)

- **A100X.1 real Telegram delivery path**: introduced `ITelegramMessageSender` with Host transport-backed `TelegramMessageSender`; `TelegramSendTool` now reflects actual transport success/failure (including retryability and latency metadata) instead of log-only success.
- **A100X.2 real scorecard evidence path**: `AutonomyScorecardGateTests` now executes benchmark scenarios over the message bus before scoring (no seeded-only scorecard input).
- **A100X.3 profile enforcement documentation consistency**: stale execution-profile enforcement comment updated to match runtime enforcement.
- **A100X.4 C# hot-path optimization**: authorization profile command allowlists are now cached as immutable frozen snapshots for lower per-call overhead.
- **Validation**: Telegram tool tests (4/4), Host autonomy/authorization targeted tests (11/11), perf gate pass, full solution regression green (`EXIT:0`).

## ✅ Strict Autonomy Closure (A101, Aug 3, 2026)

- **A101.1 saga real-flow closure**: `CreateCollaborationSaga` no longer uses placeholder send/aggregate logic.
  - `SendCollaborationRequestsStep` now executes real collaboration dispatch through `IAgentCollaborationService.RequestCollaborationAsync` and stores `CollaborationRequestResult`.
  - Compensation now performs real `CancelCollaborationAsync` when a valid request id is available.
  - `AggregateResponsesStep` now consumes the real collaboration result from context and fails fast if the upstream step contract is broken.
- **A101.2 autonomy evidence closure**: `AutonomyScorecardGateTests` now run real benchmark scenarios end-to-end via Host Playground API (`/api/playground/scenarios`, `/run`) using `InfernalHierarchyTestWebAppFactory`.
  - Gate pass/fail is now derived from real orchestrated agent execution traces and timings, not synthetic responder-seeded outputs.
  - Deterministic CI behavior is retained through benchmark tagging and controlled prompt/time budget parameters.
- **Validation**:
  - Targeted suites: saga execution/coverage + host autonomy gate all passing.
  - Full regression: 902 tests passed, 0 failed.

## ✅ Continuous Optimization Closure (A101.3, Aug 3, 2026)

- **A101.3 hot-path allocation reduction**: optimized cross-instance federation aggregation internals in `FederationService`.
  - Replaced several high-frequency LINQ order/group/select chains with single-pass loop selection for Voting, WeightedVoting, HighestConfidence, and Hierarchical strategy winner resolution.
  - Replaced LINQ `GroupBy` projection with a dictionary-backed response accumulator to reduce temporary enumerable/object allocations while preserving aggregation semantics.
- **Validation**:
  - `FederationServiceTests`: 17/17 pass.
  - Perf gate: PASS with budget compliance after optimization (`federationAggregation` latency/op 0.161ms, alloc/op 33133B).
  - Full regression: 902/902 pass.

## ✅ Strict Autonomy Runtime Closure (A102, Aug 3, 2026)

- **A102.1 executable adjudication workflow**:
  - `AgentCollaborationService` now executes an autonomous supervisor adjudication workflow when conflicts remain unresolved after bounded rounds.
  - `FederationService` now executes the same autonomous adjudication workflow for unresolved cross-instance outcomes, returning terminal autonomous decisions instead of action-token-only escalation.
- **A102.2 custom-tool autonomy closure**:
  - `CreateCustomToolTool` no longer hard-blocks creation/registration on manual-approval branches.
  - `CustomToolsStartupService` no longer blocks loading policy-flagged custom tools pending manual approval; tools are compiled/loaded autonomously when policy allows.
- **Validation**:
  - Added local unresolved-consensus adjudication test in `AgentCollaborationServiceTests`.
  - Updated federated unresolved-path tests to assert autonomous adjudication outcomes.
  - Updated custom-tool creation tests to assert compile/register behavior without manual-approval gate.
  - Added startup reload test proving policy-flagged tool load without manual gate.
  - Full regression: 904/904 pass.

## ✅ Performance Headroom Closure (A103.1, Aug 3, 2026)

- **A103.1 federation allocation headroom improvement**:
  - Reduced temporary allocations in `FederationService.RequestCrossInstanceCollaborationAsync` by replacing the concurrent-bag + LINQ task pipeline with pre-sized list collection and explicit task wiring.
  - Reduced aggregation materialization overhead by tracking weighted totals in `ResponseAccumulator` (no per-group item list retention for weighted voting).
  - Preserved deterministic behavior with stable adjudication tie-break (`AgentId`) when rank/confidence are equal.
- **Validation**:
  - Perf gate: PASS (`federationAggregation` latency/op 0.153ms; alloc/op 32157B, budget 35000B).
  - Regression improvement vs previous measured alloc/op 33133B: **-976B/op**.
  - Targeted federation tests: 20/20 pass.
  - Full regression: 904/904 pass.

## ✅ Federation Micro-Latency/Parsing Pass (A103.2, Aug 2, 2026)

- **A103.2 response extraction and routing pass**:
  - Replaced active-instance LINQ snapshot/filter in `RequestCrossInstanceCollaborationAsync` with explicit snapshot builder (`GetActiveRemoteInstancesSnapshot`) to reduce per-call iterator overhead.
  - Reworked `TryExtractAgentResponse` into a single-pass payload scan (Decision/Response/AgentId/Reasoning/Confidence), avoiding repeated per-key dictionary scans.
  - Kept deterministic adjudication tie-break behavior intact.
- **Validation**:
  - Targeted federation tests: 20/20 pass.
  - Perf gate: PASS (`federationAggregation` latency/op 0.156ms; alloc/op 31877B, budget 35000B).
  - Allocation improvement vs A103.1 snapshot (32157B/op): **-280B/op**.
  - Full regression: 904/904 pass.

### Implemented Tests (existing)
- Host: integration tests
- Agents: unit tests
- Messaging: concurrency tests
- Memory: CRUD tests
- Tools: tool execution tests
- Telegram: basic bot tests
- Personas: loader tests
- Core: entity tests

### Newly Added Test Coverage (Feb 3, 2026)
- VectorMemoryService
- MemoryPruningService
- MultiModelLlmClient
- EventStore
- Health checks
- Security services
- Observability

---

## ✅ Completed “Could Have” Items (selected)
- Agent specialization system
- Agent learning
- Agent templates
- Agent suspension/hibernation
- Real embedding models
- Memory versioning
- Cross-agent memory sharing
- Multi-tenancy
- Federation
- GraphQL API
- CQRS
- Saga pattern

---

## ✅ Roadmap Sync (Aug 1, 2026)

The roadmap in `TODO.md` was synchronized so completed work is now tracked here.

### P0-P2 Delivery Blocks Completed ✅
- P0 correctness/safety: broadcast fan-out, queue/backpressure policy, non-local endpoint auth guard, runtime execution limits.
- P1 production readiness: resilience policy wiring, dead-letter + replay budget, CI/release workflows, GraphQL status ADR.
- P1 workflow/teamwork: quality script, test taxonomy, change-impact matrix, incident runbooks, skill catalog governance, collaboration artifact persistence, conflict protocol, ReAct checkpoints, collaboration templates.
- P2 observability/hygiene: correlation/causation propagation, queue/supervisor/tool-timeout metrics, actionable readiness payloads, startup inert-config warnings, active feature matrix, SLOs and alert playbooks.

### P3 Tools and Ecosystem (Implemented) ✅
- GraphQL-first integration tooling and auth helpers:
  - Added `graphql_request` tool with host allowlist, auth header helpers, read-only operation guardrails, and optional introspection blocking.
  - Configured via `GraphQlTool` options and `ToolPermissions`.
- Read-only SQL query tool with strict guardrails:
  - Added `sql_query_readonly` tool with single-statement/read-only enforcement, forbidden keyword checks, row/cell limits, and allowlisted connection string policy.
  - Configured via `SqlReadOnlyTool` options and `ToolPermissions`.
- Custom tool management meta-tools:
  - Added `custom_tool_list` and `custom_tool_delete` tools.
  - Added runtime tool unregister support in `IToolRegistry` and persisted delete support in `ICustomToolStore`.

### P3 Voice and UX (Incremental) ✅
- French TTS quality improvements with language-specific voice selection:
  - Added optional routing by language for `tts_speak` (`language` parameter + lightweight auto-detection for French text).

## ✅ Remaining TODO Closure (Aug 2, 2026)

### P0.3 Autonomous incident response baseline ✅
- Added `AutonomousIncidentResponseService` to monitor critical degradation signals:
  - tool timeout spikes,
  - queue rejection growth,
  - stalled/looping branch detections.
- Added controlled mitigations:
  - root replan requests,
  - branch preemption for looping non-root agents,
  - temporary incident throttle for selected high-amplification tools.
- Added explicit event audit (`incident.response`) and metrics namespace (`incident_response.actions.*`).

### P1.2 Autonomous skill/tool synthesis pipeline ✅
- Confirmed end-to-end custom tool synthesis chain (synthesize → policy scan → compile → persist → register).
- Hardened overwrite safety with rollback: failed recompilation now restores previous persisted definition.

### P1.3 Persistent runtime skills and reusable skillbook writer ✅
- Runtime skill grants persisted in LiteDB and validated across restarts.
- Skillbook outcome publisher promotes reusable capability entries with provenance metadata and versioning.
  - Added French Piper overrides in `TextToSpeech` options (`FrenchPiperVoicePath`, `FrenchPiperSpeakerId`).
  - Added per-voice Piper model caching/warmup support so default and French voices can both be preloaded and reused.

### P3 LLM and Multimodal (Incremental) ✅
- Model routing policy by task type and latency budget:
  - Added routing policy options in Ollama config (`EnableModelRoutingPolicy`, `ModelRoutes`).
  - Added routing capability contract (`IModelRoutingLlmClient`) with typed hint (`LlmRoutingHint`).
  - Implemented policy-driven model selection in `OllamaClient` for both streaming and non-streaming calls.
  - Wired Voice Copilot to pass task type + latency budget hints so low-latency voice replies can target faster models.

### P3 Strategic Enhancements (Completed) ✅
- Vision-model support for image-aware tasks:
  - Added `IImageLlmClient` optional capability and Ollama multimodal request path.
  - Added `vision_describe` tool with strict local-root/extension/size constraints and bounded outputs.
  - Added `Vision` options + startup validation.
- Voice sidecar mode:
  - Added sidecar routing options to `VoiceTranscription` and `TextToSpeech`.
  - Added optional HTTP delegation path in `audio_transcribe` and `tts_speak` while preserving local execution fallback.
  - Added `voice_sidecar` health check for readiness diagnostics.
- Agent playground:
  - Added in-memory scenario/run store service (`AgentPlaygroundService`).
  - Added operator APIs for create/list/run/replay (`/api/playground/*`).
  - Added UI page `/ui/playground` for quick scenario simulation.
- Reasoning/tool timeline debugging views:
  - Added timeline API (`/api/perf/timeline`) merging task/tool/reasoning checkpoint signals.
  - Emitted ReAct checkpoints into event stream for timeline continuity.
  - Added UI page `/ui/timeline` to inspect timeline entries and metadata.
- Plugin SDK for contributors:
  - Added starter scaffold in `templates/plugin-sdk`.
  - Added onboarding guide `Documentation/Plugin-SDK.md`.

### Validation ✅
- New targeted tests added and passing:
  - `GraphQlRequestToolTests`
  - `SqlReadOnlyQueryToolTests`
  - `CustomToolManagementToolsTests`
  - `TextToSpeechLanguageRoutingTests`
  - `OllamaModelRoutingPolicyTests`
  - `VisionDescribeToolTests`
  - `VoiceAndVisionOptionsValidatorTests`
- Impacted host/tool tests also pass with interface updates.
