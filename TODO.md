# InfernalHierarchy - TODO Implementation Plan

> Last Updated: 2026-08-01
> Objective: deliver the most autonomous, resilient, and optimized Telegram-first multi-agent platform.

Use this file as the active implementation plan.
Move completed work to COMPLETED.md only after code, tests, and validation are finished.

---

## Product Goal (North Star)

Build a team of agents that can:

- Receive any Telegram request.
- Autonomously execute work from simple research to full build/deploy workflows.
- Detect missing capabilities and autonomously create/upgrade the required tools/skills.
- Persist successful new capabilities into a reusable catalog/skillbook for future tasks.
- Self-heal under failure with strong resilience and high performance.

---

## Execution Order

1. P0 - Autonomous resilience closure (must-have first)
2. P1 - Autonomous capability closure and persistence
3. P2 - Safe full-delivery autonomy (build/deploy profiles)
4. P3 - Scale and optimization hardening
5. P4 - Validation, SLO gates, and continuous improvement loop

---

## P0 - Autonomous Resilience Closure

### P0.1 Durable dead-letter storage

Status: TODO

Implementation:

- Replace in-memory failed operation store with persistent storage (LiteDB-backed implementation of IFailedOperationStore).
- Preserve current record schema, retry budget semantics, and metrics behavior.
- Ensure startup hydration of pending replay state.

Acceptance Criteria:

- Pending failed operations survive process restart/crash.
- Replay attempts and statuses remain consistent across restarts.
- Existing dead-letter API behavior remains backward compatible.

Validation:

- Unit tests for persistence round-trip.
- Integration tests for restart recovery of pending entries.

### P0.2 Autonomous replay worker

Status: TODO

Implementation:

- Add background replay service that continuously drains replayable failed operations.
- Use exponential backoff + jitter + bounded retry budget.
- Add guardrails to avoid replay storms and feedback loops.

Acceptance Criteria:

- Transient failures replay without operator action.
- Replay worker halts cleanly on permanent failures and marks reason codes.
- Replay throughput and failure counters are visible in metrics.

Validation:

- Chaos tests with simulated dependency outages.
- Replay convergence tests under load.

### P0.3 Autonomous incident response baseline

Status: TODO

Implementation:

- Add automatic mitigation hooks for common failure patterns:
	- tool timeout spikes,
	- queue rejection growth,
	- stalled branch detection.
- Trigger controlled recovery actions (replan, preempt branch, temporary rate reduction).

Acceptance Criteria:

- System can recover from transient degradation without manual intervention in common scenarios.
- Automatic actions are fully audited in events and metrics.

Validation:

- End-to-end failure injection scenarios with expected recovery traces.

---

## P1 - Autonomous Capability Closure And Persistence

### P1.1 Capability gap analyzer

Status: TODO

Implementation:

- Introduce a pre-execution capability gap analysis stage.
- Compare task requirements vs available tools + active skills + profile constraints.
- Produce structured remediation actions:
	- create custom tool,
	- request temporary skill pack,
	- escalate to collaboration strategy,
	- switch execution profile.

Acceptance Criteria:

- Missing capability is detected without requiring explicit user wording.
- Remediation path is deterministic and explainable.
- Gap decisions are logged with reason codes.

Validation:

- Task scenarios where required capability is absent at start.
- Regression tests ensuring no false positives on already-supported tasks.

### P1.2 Autonomous skill/tool synthesis pipeline

Status: TODO

Implementation:

- Standardize generated custom tool lifecycle:
	- synthesize,
	- policy scan,
	- compile,
	- sandbox validate,
	- runtime register.
- Add promotion criteria for reusable capability candidates.

Acceptance Criteria:

- Agent can create and successfully use missing tools inside one task lifecycle.
- Failed synthesis attempts produce actionable diagnostics and safe rollback.

Validation:

- Integration tests with at least 3 missing-capability scenarios.

### P1.3 Persistent runtime skills and reusable skillbook writer

Status: TODO

Implementation:

- Persist runtime skill grants (not in-memory only).
- Add skillbook publisher service to automatically write reusable skill packs from validated successful outcomes.
- Attach provenance metadata:
	- source task,
	- risk level,
	- success count,
	- last validated date.

Acceptance Criteria:

- Skills survive restart and are reusable in future sessions.
- Auto-published skill entries are versioned and auditable.
- Promotion thresholds prevent noisy/low-quality skills from polluting catalog.

Validation:

- Persistence tests for grants and catalog entries.
- E2E scenario proving reuse on later independent task.

---

## P2 - Safe Full-Delivery Autonomy (Build/Deploy)

### P2.1 Execution profiles

Status: TODO

Implementation:

- Introduce explicit execution profiles:
	- Research profile,
	- Build profile,
	- Deploy profile.
- Each profile defines allowed tools, file scopes, network scopes, and command allowlists.

Acceptance Criteria:

- Profile selection is explicit per task and enforced at tool authorization layer.
- Build/deploy workflows run autonomously when profile permits.
- Unsafe operations are denied with clear reason codes.

Validation:

- Policy enforcement tests for each profile.
- E2E pipeline simulation using build profile.

### P2.2 Build/deploy workflow primitives

Status: TODO

Implementation:

- Add reusable workflow tools/templates for:
	- repo analysis,
	- dependency install,
	- build/test/lint,
	- packaging,
	- deploy adapters (controlled environments only).
- Add rollback hooks for failed deployment attempts.

Acceptance Criteria:

- Agent can complete representative software delivery workflow end-to-end in controlled sandbox.
- Failures auto-trigger rollback or safe stop.

Validation:

- Scenario tests for success path + failed deployment path.

---

## P3 - Scale And Optimization Hardening

### P3.1 Adaptive concurrency and planning depth

Status: TODO

Implementation:

- Add task complexity classifier.
- Tune ReAct iteration budget and parallel branch count dynamically.
- Enable controlled parallel tool execution for independent actions.

Acceptance Criteria:

- Throughput improvement under multi-task load.
- No regression in task success quality.

Validation:

- Load test benchmark before/after changes.

### P3.2 Queue and execution backpressure intelligence

Status: TODO

Implementation:

- Add dynamic backpressure reactions:
	- temporary queue policy shift,
	- per-agent throttling,
	- selective branch deferral.
- Expose actionable queue saturation diagnostics.

Acceptance Criteria:

- Queue rejection spikes are reduced under burst traffic.
- System remains responsive for high-priority tasks.

Validation:

- Burst load tests with target rejection/latency thresholds.

### P3.3 Latency and token-cost optimization loop

Status: TODO

Implementation:

- Expand model routing policies with feedback from observed latency and success.
- Add caching/prompt compaction strategies where safe.
- Add profiling reports for expensive tool/LLM paths.

Acceptance Criteria:

- Reduced p95 latency and token cost on repeated workloads.
- No drop in completion quality metrics.

Validation:

- Comparative benchmark suite (baseline vs optimized).

---

## P4 - Validation, SLO Gates, And Continuous Improvement

### P4.1 SLO enforcement in CI/CD

Status: TODO

Implementation:

- Add quality gates for:
	- dead-letter backlog growth,
	- replay success ratio,
	- queue reject rate,
	- task completion latency.
- Fail pipeline when thresholds are exceeded.

Acceptance Criteria:

- CI surfaces reliability regressions before merge/release.

Validation:

- Synthetic failing checks in CI to verify guardrails.

### P4.2 Autonomy scorecard and regression suite

Status: TODO

Implementation:

- Define autonomy benchmark scenarios:
	- simple search,
	- missing-tool task,
	- multi-step build task,
	- partial failure recovery task.
- Produce per-release autonomy score report.

Acceptance Criteria:

- Objective autonomy score trends upward release over release.

Validation:

- Automated scenario suite integrated into full lane.

### P4.3 Ops transparency and explainability

Status: TODO

Implementation:

- Add operator timeline clarity for:
	- why a skill/tool was created,
	- why replay happened,
	- why execution profile switched,
	- why a branch was preempted.

Acceptance Criteria:

- Every autonomous decision has traceable reason codes and event lineage.

Validation:

- Operator UAT checklist for debuggability.

---

## Immediate Next Sprint (start here)

1. Implement durable failed-operation store.
2. Implement autonomous replay worker.
3. Implement capability gap analyzer.
4. Persist runtime skill grants.
5. Add integration tests for restart recovery and missing-capability closure.

---

## Done Criteria (for each item)

An item can move to COMPLETED.md only when all are true:

1. Code implemented.
2. Unit/integration tests added and passing.
3. Full regression green.
4. Metrics and logs updated where relevant.
5. Documentation updated (runbook/feature matrix/README if impacted).
