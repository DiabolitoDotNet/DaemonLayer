using System.Collections.Concurrent;

namespace InfernalHierarchy.Tools.Telemetry;

/// <summary>
/// Tracks token usage and costs across all LLM calls for analysis and optimization
/// </summary>
public class TokenUsageTracker
{
    private readonly ConcurrentBag<TokenUsageRecord> _usageRecords = new();
    private readonly ILogger<TokenUsageTracker> _logger;
    private readonly object _statsLock = new();

    private int _totalInputTokens;
    private int _totalOutputTokens;
    private TimeSpan _totalDuration;
    private readonly Dictionary<string, ModelUsageStats> _modelStats = new();

    public TokenUsageTracker(ILogger<TokenUsageTracker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Record a single LLM usage event
    /// </summary>
    public void RecordUsage(TokenUsageRecord record)
    {
        _usageRecords.Add(record);

        lock (_statsLock)
        {
            _totalInputTokens += record.InputTokens;
            _totalOutputTokens += record.OutputTokens;
            _totalDuration += record.Duration;

            if (!_modelStats.ContainsKey(record.ModelName))
            {
                _modelStats[record.ModelName] = new ModelUsageStats();
            }

            var stats = _modelStats[record.ModelName];
            stats.CallCount++;
            stats.TotalInputTokens += record.InputTokens;
            stats.TotalOutputTokens += record.OutputTokens;
            stats.TotalDuration += record.Duration;
        }

        _logger.LogDebug("Token usage: {Model} - {Input} in / {Output} out ({Ms}ms)",
            record.ModelName, record.InputTokens, record.OutputTokens, record.Duration.TotalMilliseconds);
    }

    /// <summary>
    /// Get overall usage statistics
    /// </summary>
    public TokenUsageStats GetOverallStats()
    {
        lock (_statsLock)
        {
            return new TokenUsageStats
            {
                TotalCalls = _usageRecords.Count,
                TotalInputTokens = _totalInputTokens,
                TotalOutputTokens = _totalOutputTokens,
                TotalTokens = _totalInputTokens + _totalOutputTokens,
                AverageDuration = _usageRecords.Count > 0
                    ? TimeSpan.FromMilliseconds(_totalDuration.TotalMilliseconds / _usageRecords.Count)
                    : TimeSpan.Zero,
                ModelBreakdown = _modelStats.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value)
            };
        }
    }

    /// <summary>
    /// Get usage statistics for a specific agent
    /// </summary>
    public TokenUsageStats GetAgentStats(string agentId)
    {
        var agentRecords = _usageRecords.Where(r => r.AgentId == agentId).ToList();

        if (!agentRecords.Any())
        {
            return new TokenUsageStats();
        }

        return new TokenUsageStats
        {
            TotalCalls = agentRecords.Count,
            TotalInputTokens = agentRecords.Sum(r => r.InputTokens),
            TotalOutputTokens = agentRecords.Sum(r => r.OutputTokens),
            TotalTokens = agentRecords.Sum(r => r.InputTokens + r.OutputTokens),
            AverageDuration = TimeSpan.FromMilliseconds(
                agentRecords.Average(r => r.Duration.TotalMilliseconds))
        };
    }

    /// <summary>
    /// Get usage statistics for a specific model
    /// </summary>
    public ModelUsageStats? GetModelStats(string modelName)
    {
        lock (_statsLock)
        {
            return _modelStats.TryGetValue(modelName, out var stats) ? stats : null;
        }
    }

    /// <summary>
    /// Calculate estimated cost (requires cost per token configuration)
    /// </summary>
    public decimal CalculateEstimatedCost(Dictionary<string, ModelPricing> pricing)
    {
        decimal totalCost = 0;

        lock (_statsLock)
        {
            foreach (var (modelName, stats) in _modelStats)
            {
                if (pricing.TryGetValue(modelName, out var price))
                {
                    var inputCost = (stats.TotalInputTokens / 1_000_000m) * price.InputPricePerMillion;
                    var outputCost = (stats.TotalOutputTokens / 1_000_000m) * price.OutputPricePerMillion;
                    totalCost += inputCost + outputCost;
                }
            }
        }

        return totalCost;
    }

    /// <summary>
    /// Reset all statistics
    /// </summary>
    public void Reset()
    {
        lock (_statsLock)
        {
            _usageRecords.Clear();
            _totalInputTokens = 0;
            _totalOutputTokens = 0;
            _totalDuration = TimeSpan.Zero;
            _modelStats.Clear();
        }

        _logger.LogInformation("Token usage statistics reset");
    }

    /// <summary>
    /// Get recent usage records
    /// </summary>
    public IEnumerable<TokenUsageRecord> GetRecentRecords(int count = 100)
    {
        return _usageRecords
            .OrderByDescending(r => r.Timestamp)
            .Take(count);
    }

    /// <summary>
    /// Builds optimization insights for high-latency/high-cost model paths.
    /// </summary>
    public TokenOptimizationReport GetOptimizationReport(
        int highLatencyThresholdMs = 5000,
        int minCalls = 3,
        int topN = 10)
    {
        lock (_statsLock)
        {
            var items = _modelStats
                .Select(kvp =>
                {
                    var model = kvp.Key;
                    var stats = kvp.Value;
                    var avgLatencyMs = stats.AverageDuration.TotalMilliseconds;
                    var totalTokens = stats.TotalInputTokens + stats.TotalOutputTokens;
                    var avgTokensPerCall = stats.CallCount > 0 ? (double)totalTokens / stats.CallCount : 0d;
                    var throughput = stats.TokensPerSecond;
                    var expensive = stats.CallCount >= minCalls && avgLatencyMs >= highLatencyThresholdMs;

                    var recommendation = expensive
                        ? "Consider routing this workload to a lower-latency model or adding tighter max_tokens/prompt compaction."
                        : "Model path looks healthy for current traffic.";

                    return new TokenOptimizationItem(
                        model,
                        stats.CallCount,
                        avgLatencyMs,
                        avgTokensPerCall,
                        throughput,
                        expensive,
                        recommendation);
                })
                .OrderByDescending(x => x.IsHighLatencyOrCost)
                .ThenByDescending(x => x.AverageLatencyMs)
                .ThenByDescending(x => x.CallCount)
                .Take(Math.Max(1, topN))
                .ToList();

            return new TokenOptimizationReport(items);
        }
    }
}

public class TokenUsageRecord
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ModelName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public TimeSpan Duration { get; set; }
}

public class TokenUsageStats
{
    public int TotalCalls { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public TimeSpan AverageDuration { get; set; }
    public Dictionary<string, ModelUsageStats>? ModelBreakdown { get; set; }
}

public class ModelUsageStats
{
    public int CallCount { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public TimeSpan TotalDuration { get; set; }

    public TimeSpan AverageDuration => CallCount > 0
        ? TimeSpan.FromMilliseconds(TotalDuration.TotalMilliseconds / CallCount)
        : TimeSpan.Zero;

    public double TokensPerSecond => TotalDuration.TotalSeconds > 0
        ? (TotalInputTokens + TotalOutputTokens) / TotalDuration.TotalSeconds
        : 0;
}

public class ModelPricing
{
    public string ModelName { get; set; } = string.Empty;
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
}

public sealed record TokenOptimizationReport(IReadOnlyList<TokenOptimizationItem> Items);

public sealed record TokenOptimizationItem(
    string ModelName,
    int CallCount,
    double AverageLatencyMs,
    double AverageTokensPerCall,
    double TokensPerSecond,
    bool IsHighLatencyOrCost,
    string Recommendation);
