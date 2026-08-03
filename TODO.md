# InfernalHierarchy - Final TODO (Strict 100% Autonomy)

> Last Updated: 2026-08-02
> Scope: final gap-closing backlog after strict audit.

This TODO now tracks only what still blocks a strict claim of:
- autonomous end-to-end execution,
- production-grade resilience,
- measurable optimization aligned with modern C#/.NET 10 practices.

Completed roadmap items have been moved to COMPLETED.md.

---

## Audit Result Snapshot

### Confirmed complete (already implemented + validated)

- A100R.1 federation heartbeat truthfulness.
- A100R.2 Build/Deploy tool-permission alignment + startup drift diagnostics.
- A100R.3 strategy-consistent federated aggregation.
- A100R.4 autonomous unresolved-conflict escalation path.
- A100R.5 first hot-path allocation reduction in authorization.
- Regression status: targeted suites green + full solution tests green (EXIT:0).

### Remaining blockers for strict closure

The system is functionally strong, but strict 100% autonomy and optimization are not yet fully evidenced in four areas:

1. Closed-loop collaboration strategy learning is still incomplete.
2. Perf optimization lacks benchmark-grade quantitative proof and CI guardrail.
3. Autonomy scorecard exists but is not enforced as a release gate.
4. Multi-instance chaos/failure matrix is not yet formalized for federation routing guarantees.

---

## Final Gap Backlog

### A100F.1 - Close collaboration strategy learning loop

Status: DONE
Priority: P0

Evidence:

- Source TODO remains in collaboration service (`TODO: Integrate with AgentLearningService to track strategy effectiveness`).

Implementation:

- Inject and use AgentLearningService inside collaboration outcome analysis.
- Record per-strategy signals (confidence, agreement, latency, rounds, success/failure).
- Feed these signals into future strategy selection heuristics.

Acceptance Criteria:

- No collaboration-learning TODO remains in production code.
- Strategy selection can reference historical effectiveness by task profile/risk.
- Metrics/logs expose strategy win/loss and latency trends.

Validation:

- Unit tests: collaboration outcomes write expected learning records.
- Integration tests: repeated scenarios show adaptive strategy choice changes.

Completion note (2026-08-02):

- `AgentCollaborationService` now records strategy outcomes (success, confidence, agreement, latency, rounds, participants) through `AgentLearningService`.
- Dynamic strategy selection now consults historical strategy effectiveness before fallback heuristics.
- Targeted test suite passes for adaptive strategy selection and learning persistence.

---

### A100F.2 - Quantified performance gate (latency + allocations)

Status: DONE
Priority: P0

Gap:

- Optimization changes exist, but there is no benchmark harness with pass/fail budget.

Implementation:

- Add dedicated micro-benchmark project for hot paths:
  - ToolAuthorizationService authorize path,
  - federation aggregation path (strategy-specific).
- Measure both throughput and allocations.
- Persist baseline and compare on PR/CI runs.

Acceptance Criteria:

- Benchmarks run reproducibly in CI.
- Budget thresholds are explicit (for example max allocation delta %, max p95 regression %).
- PR fails when budget regression exceeds thresholds.

Validation:

- Baseline generation + one synthetic failing benchmark test to verify gate behavior.

Completion note (2026-08-02):

- Added executable perf gate harness: `tools/InfernalHierarchy.PerfGate`.
- Gate measures latency/op + allocated bytes/op for authorization and federation aggregation paths.
- Baseline budgets are versioned in `perf-baseline.json` and enforced in CI fast lane.

---

### A100F.3 - Enforce autonomy scorecard as release gate

Status: DONE
Priority: P1

Gap:

- Scorecard service exists, but autonomy quality is not enforced as a merge/release condition.

Implementation:

- Add CI step invoking scorecard endpoint or service runner after scenario execution.
- Define minimum release bar:
  - coverage = 100% benchmark scenarios,
  - minimum grade (for example B),
  - minimum success-rate thresholds per scenario.
- Fail pipeline when below target.

Acceptance Criteria:

- CI status includes explicit autonomy scorecard result.
- Release cannot pass when autonomy thresholds are not met.

Validation:

- Add controlled failing pipeline/test fixture proving the gate blocks under-threshold runs.

Completion note (2026-08-02):

- Added explicit autonomy scorecard gate tests (`AutonomyScorecardGateTests`) covering both under-threshold fail behavior and pass behavior at release thresholds.
- CI full lane now executes a dedicated gate step filtered to these tests, making autonomy thresholds an explicit merge/release condition.

---

### A100F.4 - Federated chaos matrix for routing safety

Status: DONE
Priority: P1

Gap:

- Federation behavior is improved but strict multi-instance failure matrix coverage is not explicit.

Implementation:

- Add multi-instance integration/chaos scenarios:
  - heartbeat transport failure,
  - stale heartbeat eviction,
  - partial response quorum miss,
  - strategy tie/low-confidence conflict escalation,
  - fallback-to-local vs supervisor escalation semantics.
- Verify instance selection excludes degraded nodes after failure.

Acceptance Criteria:

- Deterministic test matrix exists for all federation fallback branches.
- No degraded instance is selected during delegation/collaboration windows.

Validation:

- Integration suite with failure injection and deterministic assertions on `ConflictReasonCode`, `NextAction`, and selected instance set.

Completion note (2026-08-02):

- Added federation chaos tests for weighted-vote tie escalation and highest-confidence low-confidence escalation.
- Hardened `DelegateTaskAsync` with ordered fallback attempts across candidates; delegation now continues to the next healthy instance after failure.
- Added deterministic test coverage for fallback selection when the lowest-load instance fails.

---

## Optional Improvement Backlog (non-blocking)

### A100F.5 - Modern C# allocation-aware snapshots

Status: TODO
Priority: P2

Objective:

- Continue .NET 10/C# optimization pass with immutable/frozen snapshots where beneficial.

Implementation ideas:

- Move repeated profile command allowlist normalization to reload-time frozen structures.
- Review high-frequency LINQ allocations in aggregation/authorization paths.
- Keep readability and deterministic error-flow first.

Acceptance Criteria:

- No behavior regression.
- Measurable allocation reduction on benchmarked paths.

Validation:

- Covered by A100F.2 benchmark harness.

---

## Definition Of Done (Strict Closure)

Strict autonomy target is considered complete only when:

- A100F.1 to A100F.4 are DONE and validated.
- A100F.2 and A100F.3 gates are active in CI.
- Full solution regression remains green after gate activation.
- COMPLETED.md is updated with evidence links and command outputs summary.
