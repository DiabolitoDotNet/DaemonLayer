# Incident Runbook: Stalled Agents

## Signals

- Repeated supervisor replan cycles.
- Agent status stuck in Thinking/ActingWithTool.
- No final report for tasks within expected timeout.

## Diagnosis

1. Inspect `/api/events` around stalled task window.
2. Query `/api/perf/traces` and `/api/perf/requests` for blocked calls.
3. Verify message bus depths and reject/drop counters in `/metrics`.
4. Check dead-letter queue via `/api/ops/deadletters`.

## Mitigation

1. Cancel or preempt impacted branches.
2. Replay relevant dead-letter entries with budget remaining.
3. Lower active load and reroute to reduced agent set.

## Rollback

1. Disable recent strategy/prompt changes.
2. Revert to previous stable deployment and replay pending tasks.
