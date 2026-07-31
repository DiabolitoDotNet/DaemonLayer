using System.Collections.Concurrent;
using System.Diagnostics;

namespace InfernalHierarchy.Host.Observability;

/// <summary>
/// Metrics collection service for monitoring system health and performance
/// </summary>
public class MetricsCollector
{
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, HistogramBuffer> _histograms = new();

    // Counters
    public void IncrementCounter(string name, long value = 1)
    {
        _counters.AddOrUpdate(name, value, (_, current) => current + value);
    }

    public long GetCounter(string name)
    {
        return _counters.TryGetValue(name, out var value) ? value : 0;
    }

    // Gauges
    public void SetGauge(string name, double value)
    {
        _gauges[name] = value;
    }

    public double GetGauge(string name)
    {
        return _gauges.TryGetValue(name, out var value) ? value : 0;
    }

    // Histograms (for tracking distributions like latency)
    public void RecordValue(string name, double value)
    {
        var buffer = _histograms.GetOrAdd(name, static _ => new HistogramBuffer(1000));
        buffer.Add(value);
    }

    public HistogramStats GetHistogramStats(string name)
    {
        if (!_histograms.TryGetValue(name, out var buffer))
        {
            return new HistogramStats();
        }

        var values = buffer.Snapshot();
        if (values.Count == 0)
        {
            return new HistogramStats();
        }

        var sorted = values.OrderBy(x => x).ToList();
        return new HistogramStats
        {
            Count = sorted.Count,
            Min = sorted.First(),
            Max = sorted.Last(),
            Mean = sorted.Average(),
            P50 = GetPercentile(sorted, 0.50),
            P95 = GetPercentile(sorted, 0.95),
            P99 = GetPercentile(sorted, 0.99)
        };
    }

    public IReadOnlyList<string> GetHistogramNames()
    {
        return _histograms.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Dictionary<string, HistogramStats> GetAllHistogramStats()
    {
        var names = _histograms.Keys.ToList();

        var result = new Dictionary<string, HistogramStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            result[name] = GetHistogramStats(name);
        }

        return result;
    }

    private static double GetPercentile(List<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }

    public Dictionary<string, object> GetAllMetrics()
    {
        var metrics = new Dictionary<string, object>();

        foreach (var counter in _counters)
        {
            metrics[$"counter.{counter.Key}"] = counter.Value;
        }

        foreach (var gauge in _gauges)
        {
            metrics[$"gauge.{gauge.Key}"] = gauge.Value;
        }

        foreach (var histogram in _histograms.Keys)
        {
            var stats = GetHistogramStats(histogram);
            metrics[$"histogram.{histogram}.count"] = stats.Count;
            metrics[$"histogram.{histogram}.mean"] = stats.Mean;
            metrics[$"histogram.{histogram}.p95"] = stats.P95;
        }

        return metrics;
    }

    public void Reset()
    {
        _counters.Clear();
        _gauges.Clear();
        _histograms.Clear();
    }

    private sealed class HistogramBuffer
    {
        private readonly double[] _values;
        private readonly object _gate = new();
        private int _count;
        private int _nextIndex;

        public HistogramBuffer(int capacity)
        {
            _values = new double[Math.Max(1, capacity)];
        }

        public void Add(double value)
        {
            lock (_gate)
            {
                _values[_nextIndex] = value;
                _nextIndex = (_nextIndex + 1) % _values.Length;
                if (_count < _values.Length)
                {
                    _count++;
                }
            }
        }

        public IReadOnlyList<double> Snapshot()
        {
            lock (_gate)
            {
                if (_count == 0)
                {
                    return Array.Empty<double>();
                }

                var snapshot = new double[_count];
                if (_count < _values.Length)
                {
                    Array.Copy(_values, snapshot, _count);
                    return snapshot;
                }

                var tailLength = _values.Length - _nextIndex;
                Array.Copy(_values, _nextIndex, snapshot, 0, tailLength);
                Array.Copy(_values, 0, snapshot, tailLength, _nextIndex);
                return snapshot;
            }
        }
    }
}

public class HistogramStats
{
    public int Count { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Mean { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double P99 { get; set; }
}

/// <summary>
/// Service for collecting and exposing application metrics
/// </summary>
public class MetricsService
{
    private readonly MetricsCollector _collector;
    private readonly Stopwatch _uptime;

    public MetricsService(MetricsCollector collector)
    {
        _collector = collector;
        _uptime = Stopwatch.StartNew();
    }

    // Agent metrics
    public void RecordAgentCreated(string rank) => _collector.IncrementCounter($"agents.created.{rank.ToLowerInvariant()}");
    public void RecordAgentTerminated(string rank) => _collector.IncrementCounter($"agents.terminated.{rank.ToLowerInvariant()}");
    public void SetActiveAgentCount(string rank, int count) => _collector.SetGauge($"agents.active.{rank.ToLowerInvariant()}", count);

    // Message metrics
    public void RecordMessageSent(string messageType) => _collector.IncrementCounter($"messages.sent.{messageType.ToLowerInvariant()}");
    public void RecordMessageReceived(string messageType) => _collector.IncrementCounter($"messages.received.{messageType.ToLowerInvariant()}");
    public void RecordMessageLatency(double milliseconds) => _collector.RecordValue("message.latency.ms", milliseconds);

    // Tool metrics
    public void RecordToolExecution(string toolName) => _collector.IncrementCounter($"tools.executed.{toolName.ToLowerInvariant()}");
    public void RecordToolSuccess(string toolName) => _collector.IncrementCounter($"tools.success.{toolName.ToLowerInvariant()}");
    public void RecordToolFailure(string toolName) => _collector.IncrementCounter($"tools.failure.{toolName.ToLowerInvariant()}");
    public void RecordToolLatency(string toolName, double milliseconds) => _collector.RecordValue($"tool.latency.{toolName.ToLowerInvariant()}.ms", milliseconds);

    // LLM metrics
    public void RecordLlmCall() => _collector.IncrementCounter("llm.calls");
    public void RecordLlmTokens(int tokens) => _collector.IncrementCounter("llm.tokens", tokens);
    public void RecordLlmLatency(double milliseconds) => _collector.RecordValue("llm.latency.ms", milliseconds);
    public void RecordLlmError() => _collector.IncrementCounter("llm.errors");

    // Memory metrics
    public void RecordMemoryWrite(string type) => _collector.IncrementCounter($"memory.write.{type.ToLowerInvariant()}");
    public void RecordMemoryRead(string type) => _collector.IncrementCounter($"memory.read.{type.ToLowerInvariant()}");
    public void SetMemorySize(long bytes) => _collector.SetGauge("memory.database.size.bytes", bytes);

    // System metrics
    public void RecordError(string source) => _collector.IncrementCounter($"errors.{source.ToLowerInvariant()}");
    public void SetUptimeSeconds() => _collector.SetGauge("system.uptime.seconds", _uptime.Elapsed.TotalSeconds);

    // Get all metrics
    public Dictionary<string, object> GetAllMetrics()
    {
        SetUptimeSeconds();
        return _collector.GetAllMetrics();
    }

    // Get latency stats
    public HistogramStats GetMessageLatencyStats() => _collector.GetHistogramStats("message.latency.ms");
    public HistogramStats GetLlmLatencyStats() => _collector.GetHistogramStats("llm.latency.ms");
    public HistogramStats GetToolLatencyStats(string toolName) => _collector.GetHistogramStats($"tool.latency.{toolName.ToLower()}.ms");
}
