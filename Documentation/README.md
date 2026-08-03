# Documentation

This folder is the "structured documentation set" for InfernalHierarchy / DaemonLayer.

## Current status snapshot (Aug 2026)

- Strict autonomy runtime blockers are closed.
- Unresolved collaboration/federation paths execute autonomous adjudication workflows.
- Custom tool create/reload paths run autonomously for policy-allowed sources (risk still auditable).
- Latest validated performance gate: `federationAggregation` 0.156ms/op, 31877B/op (budget 35000B).
- Full regression baseline: 904/904 passing tests.

The repository root already contains detailed topic docs (security, observability, advanced features, etc.). This folder provides a cohesive, top-down view and points you to the right deeper document.

## Start here

- [Solution Architecture](Solution-Architecture.md)
- [Features Catalog](Features.md)
- [Capabilities & Use Cases](Capabilities.md)
- [Runbooks](Runbooks/Custom-Tools.md)
- [Architecture Decision Records (ADRs)](ADRs/README.md)
- [Active Feature Matrix](Active-Feature-Matrix.md)
- [SLOs](SLOs.md)
- [Alert Playbooks](Alert-Playbooks.md)

## Runbooks

- [Custom Tools Runbook](Runbooks/Custom-Tools.md)
- [End-to-End Request Tracing](Runbooks/End-to-End-Request-Tracing.md)
- [Tool Authorization Debugging](Runbooks/Tool-Authorization-Debugging.md)
- [Analyzer Policy (Dev + CI)](Runbooks/Analyzer-Policy.md)
- [Incident: Startup Failures](Runbooks/Incident-Startup-Failures.md)
- [Incident: Stalled Agents](Runbooks/Incident-Stalled-Agents.md)
- [Incident: Tool Outage](Runbooks/Incident-Tool-Outage.md)
- [Incident: Memory Bloat](Runbooks/Incident-Memory-Bloat.md)
- [Incident: WebSocket Issues](Runbooks/Incident-WebSocket-Issues.md)

## Reliability Targets

- [Service Level Objectives](SLOs.md)
- [Alert Playbooks](Alert-Playbooks.md)

## Inventory

- [Active Feature Matrix](Active-Feature-Matrix.md)

## Related docs (repo root)

- [README](../README.md) — quickstart, project map, operational notes (includes configuration examples for `AgentSupervisor`, `ToolCache`, and `Critique`)
- [IMPLEMENTATION_SUMMARY](../IMPLEMENTATION_SUMMARY.md) — historical implementation summary; prefer the structured docs and runbooks above for durable guidance
- [ADVANCED_FEATURES](../ADVANCED_FEATURES.md) — higher-level/experimental features
- [OBSERVABILITY](../OBSERVABILITY.md) and [OBSERVABILITY_SUMMARY](../OBSERVABILITY_SUMMARY.md)
- [SECURITY_CONFIG](../SECURITY_CONFIG.md)
- [VOICE_INTERFACE](../VOICE_INTERFACE.md)
- [NEXT_STEPS](../NEXT_STEPS.md)
