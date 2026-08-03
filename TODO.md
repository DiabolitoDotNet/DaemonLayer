# InfernalHierarchy - Final Audit TODO

> Last Updated: 2026-08-03
> Goal: guarantee a defensible 100% autonomy claim for in-scope agent tasks, while keeping performance budgets green and code quality aligned with modern C#.

---

## 0) Final audit result

### 0.1 Revalidated GREEN now

- Strict Release analyzer build is green:
  - `dotnet build InfernalHierarchy.sln -c Release --no-restore -p:RunAnalyzersDuringBuild=true -p:EnforceCriticalWarningsAsErrors=true`
- Full solution tests are green:
  - 947 passed, 0 failed.
- PerfGate is green with evidence generated:
  - `PERF_GATE:PASS`
  - includes readiness, scorecard, concurrent remediation, soak stability, in-scope compliance, and certification tail latency.
- Certification gates remain in place and validated:
  - strict certification-mode E2E gate in CI full lane
  - fail-closed structured autonomy outcome contract
  - in-scope autonomy KPIs separated from out-of-scope paths.

### 0.2 What is still missing for an absolute “100% autonomy” statement

No release-blocking software gap remains for **in-scope autonomy**.

Remaining limits are operational/semantic, not implementation blockers:

- Out-of-scope and policy-blocked requests are intentionally non-autonomous by design.
- Some autonomy-critical paths depend on external services (for example vector backends, integrations, network availability).
- Therefore, the only technically defensible claim is:
  - **100% autonomy on certified in-scope tasks, under certified runtime prerequisites.**

---

## 1) Release-blocking items (P0)

### A1100.1 - In-scope autonomy claim remains fail-closed in CI

Status: DONE
Priority: P0

Implemented:

- Strict certification E2E gate wired in CI.
- In-scope completion/failure ratios drive autonomy compliance checks.
- Structured outcome contract is mandatory in certification mode.

---

## 2) Remaining hardening to keep objective durable

### A1110.1 - External dependency resilience certification

Status: DONE
Priority: P1

Problem:

- In-scope autonomy can still degrade when external dependencies are unavailable.

Required:

- Add explicit certification scenarios for degraded dependency modes (vector/search/integration outages).
- Gate expected autonomous fallback behavior per scenario.
- Export dependency-degraded evidence in certification artifacts.

Implemented:

- Added `autonomyDependencyDegradedModes` scenario in PerfGate covering vector/search/integration degraded modes.
- Added deterministic bounded-refusal gate checks per mode (required outcome contract, in-scope classification, `next_action=none`, no supervisor escalation).
- Included scenario results in generated PerfGate evidence artifact payload.

Done when:

- Certification evidence proves deterministic autonomous behavior (or explicit bounded refusal) under dependency degradation.

### A1110.2 - Analyzer strictness ratchet

Status: DONE
Priority: P1

Problem:

- Critical warnings are enforced as errors, but non-critical debt is still managed by budget/inventory.

Required:

- Promote selected non-critical analyzer categories to strict gate in phased increments.
- Reduce suppression marker budget from current baseline (27) with each release cycle when low-risk.

Implemented:

- Added phased non-critical warning ratchet property (`NonCriticalWarningsAsErrorsPhase1`) in build props.
- Enabled dedicated CI ratchet build step with `EnforceNonCriticalWarningsAsErrors=true`.
- Retained suppression marker budget gate and inventory sync tests for controlled budget ratcheting.

Done when:

- Warning budget trend is strictly decreasing over time without destabilizing delivery.

### A1110.3 - Perf budget trend enforcement across releases

Status: DONE
Priority: P1

Problem:

- PerfGate enforces per-run budgets, but drift trend across releases is not yet an explicit gate.

Required:

- Add CI comparison against previous certified evidence bundle.
- Fail when latency/allocation drift exceeds configured envelopes for autonomy-critical scenarios.

Implemented:

- Added trend-comparison config to perf baseline (`trendComparison`) with explicit drift envelopes.
- Added PerfGate runtime drift check against certified reference evidence bundle.
- Wired CI Performance Gate to compare current evidence against committed reference bundle and fail on envelope breaches.

Done when:

- CI blocks regressions on both absolute thresholds and release-over-release drift.

---

## 3) Definition of Done (strict)

The autonomy objective is considered fully achieved only if all remain true:

- 100% pass for certified in-scope scenarios in strict certification mode.
- Out-of-scope/policy-blocked outcomes are explicitly classified and excluded from in-scope compliance ratio.
- Full solution tests and strict analyzer build remain green.
- PerfGate remains green for representative autonomy scenarios including p95/p99 certification tail latency.
- Degraded dependency resilience scenarios are certified and fail-closed.
