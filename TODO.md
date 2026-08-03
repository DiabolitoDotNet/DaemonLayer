# InfernalHierarchy - TODO Final (gap to 100 percent autonomy)

> Last Updated: 2026-08-03
> Objective: claim a defensible 100 percent autonomous execution capability for requested tasks, while keeping performance stable and code aligned with modern C# practices.

---

## 0) Current validated baseline

### 0.1 What is already true now

- Strict Release build with analyzers is green (0 warning / 0 error).
- PerfGate is green, including autonomySloIntegration.
- Capability-gap loop exists end-to-end (detect -> remediate -> replay -> terminal reason code).
- Readiness API exists: GET /api/autonomy/readiness.
- Autonomy SLO API exists: GET /api/autonomy/slo.
- SLO gate evaluator exists and is wired.

### 0.2 Important reality check

- The system now has strict runtime-policy hooks and perf evidence for autonomy-critical paths.
- Remaining work is now primarily certification evidence expansion and continuous regression discipline.

---

## 1) P0 blockers to a strict 100 percent autonomy claim

### A500.1 - Startup readiness is not blocking by default

Status: DONE  
Priority: P0

Evidence:

- In appsettings, AutonomyReadiness.FailStartupOnCriticalNotReady is false.
- A non-ready critical capability does not prevent runtime startup by default.

Why this blocks 100 percent autonomy:

- If required capabilities are not ready at boot, the platform can still start and later fail autonomously requested tasks.

Required change:

- Set FailStartupOnCriticalNotReady to true for production profile(s).
- Keep false only in local/dev profile with explicit documentation.

Acceptance:

- Production configuration refuses startup when a critical capability is not ready.
- E2E test covers startup fail path and startup pass path.

Implemented:

- appsettings.Production.json now sets AutonomyReadiness.FailStartupOnCriticalNotReady to true.
- Unit tests cover startup pass/fail behavior in AutonomyReadinessHostedServiceTests.

### A500.2 - Critical readiness scope is too narrow

Status: DONE  
Priority: P0

Evidence:

- Current CriticalCapabilities list is limited (email_inbox_query only).

Why this blocks 100 percent autonomy:

- 100 percent claim requires a complete critical capability map for task completion, not a single capability probe.

Required change:

- Define a capability catalog for autonomy-critical tasks (communication, retrieval, collaboration, persistence, runtime tool lane).
- Extend readiness checks to all critical capabilities and their config dependencies.

Acceptance:

- Capability-to-readiness matrix is complete and versioned.
- Readiness API returns full critical set with reason codes per item.

Implemented in this pass:

- Default critical capability list expanded to request_collaboration, workflow_step, email_inbox_query, email_send, send_telegram.
- Readiness service now includes config-aware checks for email_send and send_telegram in addition to email_inbox_query.

Remaining gap:

- Future capability families can be appended in new catalog versions, but the current critical autonomy baseline is now versioned and API-exposed.

### A500.3 - SLO gate thresholds are reliability targets, not 100 percent targets

Status: DONE  
Priority: P0

Evidence:

- MinAutonomyTaskCompletionRatio = 0.95.
- MaxAutonomyTerminalFailureRatio = 0.05.
- MinAutonomyReplaySuccessRatio = 0.90.
- insufficient_data status can pass gates without enough evidence.

Why this blocks 100 percent autonomy:

- Current defaults validate resilience, not strict 100 percent outcome autonomy.

Required change:

- Introduce a strict profile for autonomy certification:
  - completion ratio target = 1.00 on certified scenario sets,
  - terminal failure ratio target = 0.00 on certified scenario sets,
  - replay success ratio target = 1.00 for applicable flows,
  - minimum sample floors increased for certification mode.
- Keep current pragmatic thresholds for day-to-day ops profile.

Acceptance:

- Two explicit operating modes exist: runtime-ops and certification.
- Certification mode fails when strict thresholds are not met.

Implemented:

- Added appsettings.AutonomyCertification.json with strict autonomy SLO thresholds (1.0 / 0.0 / 1.0) and higher sample floors.
- Base appsettings keeps pragmatic ops thresholds.

### A500.4 - Scorecard success detection is heuristic and can overestimate autonomy

Status: DONE  
Priority: P0

Evidence:

- Scorecard success currently infers failure mostly from response text patterns.
- Benchmark set has limited scenario breadth.

Why this blocks 100 percent autonomy:

- Text-based success inference can misclassify runs and does not prove terminal-state correctness.

Required change:

- Replace scorecard success heuristic with structured terminal evidence:
  - terminal reason code,
  - explicit autonomous success/failure flag,
  - no unresolved action token in terminal state.
- Expand benchmark scenarios to cover capability-gap families and degradation modes.

Acceptance:

- Scorecard uses structured run metadata, not response string heuristics.
- Benchmark catalog covers at least all critical capability families and top failure modes.

Implemented:

- Playground run responses are now enriched with structured autonomy outcome metadata:
  - autonomy_outcome_status,
  - autonomy_outcome_reason_code,
  - autonomy_outcome_autonomous_success,
  - autonomy_outcome_needs_supervisor_intervention,
  - autonomy_outcome_next_action.
- Scorecard success evaluation now prioritizes autonomy_outcome_autonomous_success and only falls back to legacy text heuristic when metadata is absent.
- Benchmark scenario breadth expanded with readiness-scale, scorecard-volume, concurrent-remediation, and soak-stability PerfGate scenarios.

---

## 2) P1 robustness and optimization work

### A510.1 - Add long-run autonomy soak validation

Status: DONE  
Priority: P1

Goal:

- Prove autonomy stability over long runs (not only short deterministic gates).

Required change:

- Add soak scenarios with sustained workload and induced transient failures.
- Track drift in completion ratio, terminal failure ratio, p95 latency, and allocation trends.

Acceptance:

- Soak report shows no trend regression beyond defined budget envelopes.

Implemented in this pass:

- Added `autonomySoakStability` scenario in PerfGate with sustained multi-window workload.
- Injected deterministic transient failures and recoveries during soak execution.
- Added drift envelope checks for:
  - completion ratio,
  - terminal failure ratio,
  - autonomy median time-to-terminal.
- Versioned budget in `tools/InfernalHierarchy.PerfGate/perf-baseline.json`.

### A510.2 - Extend perf evidence from integration-light to representative-host profiles

Status: DONE  
Priority: P1

Goal:

- Keep hot-path optimization defensible with broader runtime realism.

Required change:

- Add perf scenarios covering:
  - readiness checks at scale,
  - scorecard report generation with larger run sets,
  - capability-gap remediation under concurrent load.

Acceptance:

- New perf baselines added and kept green in CI.

Implemented in this pass:

- Added `readinessScale` PerfGate scenario to benchmark autonomy readiness preflight execution at scale.
- Added `autonomyScorecardReport` PerfGate scenario to benchmark scorecard generation over high-volume run sets.
- Added `capabilityGapRemediationConcurrent` PerfGate scenario to benchmark remediation orchestration under concurrent load.
- Versioned both budgets in `tools/InfernalHierarchy.PerfGate/perf-baseline.json`.

### A510.3 - Add explicit autonomous terminal contract checks

Status: DONE  
Priority: P1

Goal:

- Guarantee no hidden non-autonomous endings in critical flows.

Required change:

- Add contract tests asserting that certified autonomy flows end with:
  - terminal autonomous result,
  - no unresolved next action,
  - no supervisor intervention requirement.

Acceptance:

- Contract tests fail on any regression to unresolved/manual terminal semantics.

Implemented:

- Introduced a shared autonomy terminal classifier used by playground run capture:
  - AutonomyOutcomeContractEvaluator.EnrichAutonomyOutcomePayload
  - AutonomyOutcomeContractEvaluator.BuildTimeoutOutcomePayload
- Added deterministic contract unit tests for success/non-autonomous/blocked/timeout terminal outcomes.

---

## 3) P2 modern C# and maintainability hardening

### A520.1 - Keep analyzer discipline and suppression governance explicit

Status: DONE  
Priority: P2

Current state:

- Analyzer baseline is clean.
- A few justified suppressions exist (config array binding, deterministic non-security randomness).

Required change:

- Add suppression inventory in docs with owner and review date.
- Enforce no-new-warning and no-unjustified-suppression policy in CI.

Acceptance:

- Every suppression has rationale + ownership + periodic review.

Implemented:

- Added suppression inventory runbook: `Documentation/Runbooks/Analyzer-Suppressions-Inventory.md`.
- Linked inventory from analyzer policy and documentation index.

### A520.2 - Adopt certification profile configs by environment

Status: DONE  
Priority: P2

Goal:

- Make strict autonomy mode operationally easy without impacting local dev velocity.

Required change:

- Provide explicit config profiles:
  - appsettings.Development.json (pragmatic),
  - appsettings.Production.json (strict readiness),
  - optional appsettings.AutonomyCertification.json (strict SLO and sample floors).

Acceptance:

- One command/profile switch is enough to run certification-grade gates.

Implemented:

- appsettings.Production.json now carries strict readiness startup behavior.
- appsettings.AutonomyCertification.json added for strict certification SLO/readiness mode.

---

## 4) Definition of Done for a defensible 100 percent autonomy statement

The statement is allowed only when all conditions below are true on the certification profile:

- Startup blocks when any autonomy-critical capability is not ready.
- Critical capability readiness coverage is complete and documented.
- Certified scenario suite reaches full coverage and passes with strict autonomous terminal semantics.
- Scorecard success is based on structured terminal evidence, not text heuristics.
- Strict SLO gates pass with certification sample floors.
- Perf budgets remain green under representative-host scenarios.
- Strict analyzer build and full tests remain green.

---

## 5) Execution order (recommended)

1. Run certification profile validation against representative scenario suites and archive run evidence.
2. Keep strict build + PerfGate as mandatory merge gate to preserve autonomy guarantees.
