# Alert Playbooks

## Chat Availability Burn

- Signal: sustained 5xx growth or timeout growth on `/api/chat`.
- Check: `/api/perf/http`, `/health/ready`, dead-letter backlog.
- Mitigation: reduce optional integrations, inspect operator auth or upstream LLM reachability, roll back recent routing changes.

## Tool Timeout Rate

- Signal: `tools.timeout.total` growth beyond SLO threshold.
- Check: failing tool names in traces/events, dependency health, resource limits.
- Mitigation: reduce concurrency, disable slow providers, replay only safe failed operations after dependency recovery.

## Message Bus Rejections

- Signal: `message_bus.messages.rejected` non-zero or increasing.
- Check: queue depth gauges, active channel count, broadcast subscriber pressure.
- Mitigation: inspect hot producers, raise capacity only after identifying source, roll back noisy fan-out changes.

## Supervisor Intervention Storm

- Signal: `supervisor.interventions.total` or `supervisor.detected.looping` spikes.
- Check: recent ReAct checkpoints, collaboration conflict classes, dead-letter growth.
- Mitigation: preempt looping branches, request root replan, revert recent prompt or strategy changes.

## Readiness Degradation

- Signal: `/health/ready` returns degraded or unhealthy.
- Check: `summary.failingDependencies` and per-check `hint` fields.
- Mitigation: follow the matching incident runbook in Documentation/Runbooks.
