# Service Level Objectives

## Scope

These SLOs apply to the supported runtime surfaces: REST chat, WebSocket bridge, tool execution pipeline, shared memory, and operator observability endpoints.

## SLOs

| SLI | Objective | Measurement source |
|---|---|---|
| REST chat availability | 99.5% successful non-5xx responses over 30 days | `/metrics`, HTTP profiling |
| REST chat latency p95 | <= 5s over 30 days for completed requests | `http.latency.*`, `/api/perf/http` |
| Tool timeout rate | < 1% of tool executions over 7 days | `tools.timeout.total`, tool execution events |
| Dead-letter pending backlog | 0 sustained backlog older than 15 minutes | `deadletter.pending`, dead-letter API |
| Message bus rejected writes | 0 in normal operation; alert on any sustained non-zero window | `message_bus.messages.rejected` |
| Supervisor intervention rate | < 5 interventions/hour sustained | `supervisor.interventions.*` |
| Readiness success | `/health/ready` healthy or degraded with actionable dependency detail within 10s | health endpoints |

## Error Budget Use

- Burn the chat availability budget first for user-facing failures.
- Treat message bus rejections and repeated supervisor interventions as leading indicators before user-visible outage.
- Freeze risky changes when tool timeout rate or dead-letter backlog exceeds objective for two consecutive review windows.
