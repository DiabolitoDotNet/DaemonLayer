# Incident Runbook: Memory Bloat

## Signals

- Rising process memory and GC pressure.
- Slower responses and increased latency p95.
- Large growth in memory collections or dead-letter store.

## Diagnosis

1. Check `/api/perf/snapshot` and `/api/perf/histograms`.
2. Inspect memory database size and collection growth metrics.
3. Review dead-letter totals and pending counts.
4. Identify high-cardinality event payload patterns.

## Mitigation

1. Prune expired runtime grants and stale entries.
2. Reduce cache retention windows.
3. Temporarily lower throughput and parallelism.

## Rollback

1. Revert recent features that increased retained payloads.
2. Restore previous data snapshot if required.
