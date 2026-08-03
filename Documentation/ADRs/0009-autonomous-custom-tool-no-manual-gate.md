# ADR 0009: Autonomous custom-tool execution lane without manual approval gate

- Status: Accepted
- Date: 2026-08-02

## Context

Dynamic custom tools are generated, persisted, compiled, and registered at runtime. Previous behavior could hard-stop on manual approval requirements even when policy allowed execution, creating a human-in-the-loop blocker.

This gate prevented strict autonomous execution claims for policy-allowed custom tool workflows and startup reload paths.

## Decision

Remove required manual-approval blocking from policy-allowed custom-tool create/reload flows.

- `create_custom_tool` creation path no longer returns terminal failure solely because manual approval is missing.
- startup custom-tool reload no longer skips policy-allowed tools due to missing manual approval.
- policy risk indicators (`RequiresManualApproval`, matched policy rules) remain persisted and observable for audit/risk reporting.
- policy denial still blocks execution; this ADR does not relax deny decisions.

## Consequences

Positive:

- eliminates required human intervention from supported custom-tool execution lane,
- preserves auditability while enabling full autonomy for policy-allowed sources,
- improves runtime continuity on startup/reload.

Trade-offs:

- organizations relying on mandatory human approval must enforce that requirement externally (process/policy),
- stronger observability and review discipline is required since execution can proceed without approval checkpoints,
- risk posture depends on quality of policy rules and runtime permissions.

## Notes / Links

- Related code:
  - `src/InfernalHierarchy.Tools/Tools/Meta/CreateCustomToolTool.cs`
  - `src/InfernalHierarchy.Host/Tools/CustomToolsStartupService.cs`
- Related tests:
  - `tests/InfernalHierarchy.Tools.Tests/CreateCustomToolToolTests.cs`
  - `tests/InfernalHierarchy.Host.Tests/CustomToolsStartupServiceTests.cs`
