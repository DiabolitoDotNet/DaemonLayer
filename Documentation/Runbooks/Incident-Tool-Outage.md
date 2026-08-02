# Incident Runbook: Tool Outage

## Signals

- Spike in tool failures/timeouts.
- Dead-letter growth for ToolExecution records.
- Increased `resource_limit_timeout` metadata in tool responses.

## Diagnosis

1. Check `/metrics` for dead-letter and tool failure counters.
2. Inspect event stream for failing tool names and reason codes.
3. Verify external dependency health endpoints and network reachability.
4. Review retry/circuit breaker behavior in logs.

## Mitigation

1. Disable failing optional tool/provider.
2. Replay safe dead-letter entries after dependency recovery.
3. Route tasks to fallback providers/tools when available.

## Rollback

1. Revert recent tool changes or provider config.
2. Restore previous image and monitor failure counters.
