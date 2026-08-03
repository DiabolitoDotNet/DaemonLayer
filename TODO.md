# InfernalHierarchy - TODO Final Verification Pass

> Last Updated: 2026-08-03
> Objective: reach a defensible 100 percent autonomous execution claim for in-scope tasks, while keeping performance optimized and aligned with modern C# practices.

---

## 0) Ground truth from this final pass

### 0.1 Revalidated as GREEN

- Strict Release build with analyzers and critical warning enforcement passes.
- PerfGate passes, including certification tail latency (avg/p95/p99) and evidence JSON output.
- Certification primitives are implemented: fail-closed insufficient-data handling, structured outcome contract checks, scope classification, manifest drift tests.
- Certification/governance tests run in Host suite pass on targeted run.
- Dedicated strict certification-mode E2E gate is implemented and wired in CI full lane.
- In-scope autonomy KPIs are implemented, exposed, and consumed by autonomy SLO gates.

### 0.2 Claim posture after this pass

- The previously identified P0 blockers are now implemented.
- Remaining work is continuous hardening, not a release-blocking autonomy gap.

---

## 1) Remaining blockers

### A1000.1 - Add strict certification E2E gate in CI

Status: DONE
Priority: P0

Problem:

- Full lane runs `AutonomyScorecardGateTests`, but those tests currently validate release thresholds through `GenerateReport(runsPerScenario)` and do not explicitly enforce strict certification-mode options as a release gate.

Required:

- Add CI step (or dedicated test class) that evaluates scorecard with strict options:
  - `CertificationMode=true`
  - `FailOnInsufficientData=true`
  - `RequireStructuredOutcomeContract=true`
- Fail pipeline if certification pass flag is false.

Implemented:

- Added strict certification-mode E2E test in Host test suite.
- Added explicit CI full-lane step (`Autonomy Certification Strict Gate`) that runs this strict test as a release blocker.

Done when:

- CI contains an explicit strict certification-mode gate with deterministic pass/fail semantics.

### A1000.2 - Separate in-scope autonomy KPI from out-of-scope outcomes

Status: DONE
Priority: P0

Problem:

- Out-of-scope events are tracked, but "100 percent autonomy" claims are still harder to defend when global failure semantics include out-of-scope terminal paths.

Required:

- Introduce explicit in-scope denominator KPIs/gates (for example `autonomy_in_scope_completion_ratio`, `autonomy_in_scope_terminal_failure_ratio`).
- Keep out-of-scope ratio audited separately and excluded from in-scope autonomy compliance checks.

Implemented:

- Added in-scope counters and derived gauges in capability-gap metrics sink.
- Switched autonomy SLO gate evaluation to in-scope denominators/ratios.
- Exposed in-scope KPI values in autonomy SLO API payload.
- Added tests proving out-of-scope-heavy traffic does not fail in-scope autonomy gates.

Done when:

- SLO/scorecard certification can prove 100 percent autonomy for in-scope tasks independently of legitimate out-of-scope refusals.

---

## 2) Optimization and modern C# continuous hardening

### A1010.1 - Keep analyzer suppression debt on a shrinking trend

Status: DONE
Priority: P1

Required:

- Continue replacing justified suppressions with code fixes when low risk.
- Keep suppression inventory and regression test in sync.

Implemented:

- Added suppression inventory bidirectional sync test (inventory -> source and source -> inventory).
- Added suppression marker budget gate (`Suppression marker budget (src): 27`) enforced by test.
- Kept inventory aligned with current suppression footprint and documented regression budget.

Done when:

- No new broad suppressions; measurable net reduction over time.

### A1010.2 - Keep perf budgets representative as autonomy scope grows

Status: DONE
Priority: P1

Required:

- Extend PerfGate scenarios whenever new autonomy-critical workflows are added.
- Keep p95/p99 and allocation budgets versioned and enforced.

Implemented:

- Added PerfGate scenario `autonomyInScopeCompliance` to validate in-scope autonomy gate behavior under out-of-scope-heavy traffic.
- Added perf baseline version contract (`baselineVersion`) validated at runtime.
- Updated baseline budgets and docs to include in-scope compliance and certification tail-latency coverage.

Done when:

- Every new certified autonomy capability has corresponding perf evidence and gate coverage.

---

## 3) Final Definition of Done

The 100 percent autonomy claim is acceptable only if all are true:

- Strict certification-mode E2E gate is mandatory and green in CI.
- In-scope autonomy KPIs are measured and gated independently of out-of-scope refusals.
- Structured terminal contract validation is mandatory in certification mode.
- Capability manifest/readiness drift checks, PerfGate evidence, strict build, and full tests remain green.