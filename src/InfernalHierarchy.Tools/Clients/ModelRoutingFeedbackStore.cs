using System.Collections.Concurrent;

namespace InfernalHierarchy.Tools.Clients;

public interface IModelRoutingFeedbackStore
{
    void RecordOutcome(string modelName, bool success, TimeSpan duration, int outputTokens);

    /// <summary>
    /// Returns a non-negative penalty score for routing decisions (lower is better).
    /// </summary>
    double GetPenalty(string modelName);

    IReadOnlyDictionary<string, ModelRoutingFeedbackSnapshot> GetSnapshots();
}

public sealed record ModelRoutingFeedbackSnapshot(
    string ModelName,
    long Calls,
    long Failures,
    double FailureRate,
    double AvgLatencyMs,
    double AvgOutputTokens,
    double Penalty);

public sealed class InMemoryModelRoutingFeedbackStore : IModelRoutingFeedbackStore
{
    private sealed class Aggregate
    {
        public long Calls;
        public long Failures;
        public long TotalLatencyMs;
        public long TotalOutputTokens;
    }

    private readonly ConcurrentDictionary<string, Aggregate> _aggregates =
        new(StringComparer.OrdinalIgnoreCase);

    public void RecordOutcome(string modelName, bool success, TimeSpan duration, int outputTokens)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return;
        }

        var key = modelName.Trim();
        var aggregate = _aggregates.GetOrAdd(key, static _ => new Aggregate());

        Interlocked.Increment(ref aggregate.Calls);
        if (!success)
        {
            Interlocked.Increment(ref aggregate.Failures);
        }

        var latencyMs = Math.Max(0, (long)duration.TotalMilliseconds);
        Interlocked.Add(ref aggregate.TotalLatencyMs, latencyMs);
        Interlocked.Add(ref aggregate.TotalOutputTokens, Math.Max(0, outputTokens));
    }

    public double GetPenalty(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) || !_aggregates.TryGetValue(modelName.Trim(), out var aggregate))
        {
            return 0d;
        }

        var calls = Math.Max(1, Interlocked.Read(ref aggregate.Calls));
        var failures = Interlocked.Read(ref aggregate.Failures);
        var failureRate = (double)failures / calls;
        var avgLatencyMs = (double)Interlocked.Read(ref aggregate.TotalLatencyMs) / calls;

        // Strongly penalize unreliable models first, then high latency models.
        return (failureRate * 100d) + (avgLatencyMs / 250d);
    }

    public IReadOnlyDictionary<string, ModelRoutingFeedbackSnapshot> GetSnapshots()
    {
        var snapshots = new Dictionary<string, ModelRoutingFeedbackSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var (modelName, aggregate) in _aggregates)
        {
            var calls = Math.Max(1, Interlocked.Read(ref aggregate.Calls));
            var failures = Interlocked.Read(ref aggregate.Failures);
            var failureRate = (double)failures / calls;
            var avgLatencyMs = (double)Interlocked.Read(ref aggregate.TotalLatencyMs) / calls;
            var avgOutputTokens = (double)Interlocked.Read(ref aggregate.TotalOutputTokens) / calls;

            snapshots[modelName] = new ModelRoutingFeedbackSnapshot(
                ModelName: modelName,
                Calls: calls,
                Failures: failures,
                FailureRate: failureRate,
                AvgLatencyMs: avgLatencyMs,
                AvgOutputTokens: avgOutputTokens,
                Penalty: GetPenalty(modelName));
        }

        return snapshots;
    }
}