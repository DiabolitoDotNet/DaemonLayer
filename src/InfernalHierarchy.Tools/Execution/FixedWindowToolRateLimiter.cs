using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace InfernalHierarchy.Tools.Execution;

public sealed class FixedWindowToolRateLimiter : IToolRateLimiter
{
    private sealed class WindowCounter
    {
        private readonly object _gate = new();

        public long WindowStartUnixSeconds { get; private set; }
        public int CountInWindow { get; private set; }
        public long LastSeenUnixSeconds { get; private set; }

        public RateLimitDecision TryAcquire(long nowUnixSeconds, FixedWindowRateLimitRule rule)
        {
            if (rule.PermitLimit <= 0 || rule.WindowSeconds <= 0)
            {
                LastSeenUnixSeconds = nowUnixSeconds;
                return RateLimitDecision.Allow();
            }

            lock (_gate)
            {
                LastSeenUnixSeconds = nowUnixSeconds;

                if (WindowStartUnixSeconds == 0)
                {
                    WindowStartUnixSeconds = nowUnixSeconds;
                    CountInWindow = 0;
                }

                var windowEnd = WindowStartUnixSeconds + rule.WindowSeconds;
                if (nowUnixSeconds >= windowEnd)
                {
                    WindowStartUnixSeconds = nowUnixSeconds;
                    CountInWindow = 0;
                    windowEnd = WindowStartUnixSeconds + rule.WindowSeconds;
                }

                if (CountInWindow < rule.PermitLimit)
                {
                    CountInWindow++;
                    return RateLimitDecision.Allow();
                }

                var retryAfterSeconds = Math.Max(1, (int)(windowEnd - nowUnixSeconds));
                return RateLimitDecision.Deny(
                    retryAfter: TimeSpan.FromSeconds(retryAfterSeconds),
                    reason: $"Rate limit exceeded ({rule.PermitLimit}/{rule.WindowSeconds}s)");
            }
        }
    }

    private readonly IOptions<ToolRateLimitingOptions> _options;
    private readonly ILogger<FixedWindowToolRateLimiter> _logger;
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new(StringComparer.OrdinalIgnoreCase);
    private int _checksSincePrune;

    public FixedWindowToolRateLimiter(
        IOptions<ToolRateLimitingOptions> options,
        ILogger<FixedWindowToolRateLimiter>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<FixedWindowToolRateLimiter>.Instance;
    }

    public RateLimitDecision Check(ToolExecutionContext context)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            return RateLimitDecision.Allow();
        }

        if (string.IsNullOrWhiteSpace(context.AgentId))
        {
            return RateLimitDecision.Allow();
        }

        var toolOverride = options.Tools.TryGetValue(context.ToolName, out var ov) ? ov : null;
        if (toolOverride?.Enabled == false)
        {
            return RateLimitDecision.Allow();
        }

        var rank = string.IsNullOrWhiteSpace(context.AgentRank) ? "Worker" : context.AgentRank;
        var rule = ResolveRule(options, toolOverride, rank);

        var key = $"{context.AgentId}:{context.ToolName}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        MaybePrune(options, now);

        var counter = _counters.GetOrAdd(key, _ => new WindowCounter());
        var decision = counter.TryAcquire(now, rule);

        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "⏱️ Tool rate limited | AgentId={AgentId} Rank={Rank} Tool={Tool} RetryAfterMs={RetryAfterMs}",
                context.AgentId,
                rank,
                context.ToolName,
                (long)decision.RetryAfter.TotalMilliseconds);
        }

        return decision;
    }

    private static FixedWindowRateLimitRule ResolveRule(
        ToolRateLimitingOptions options,
        ToolRateLimitOverride? toolOverride,
        string rank)
    {
        if (toolOverride?.RankDefaults != null && toolOverride.RankDefaults.TryGetValue(rank, out var toolRankRule))
        {
            return toolRankRule;
        }

        if (toolOverride?.DefaultRule != null)
        {
            return toolOverride.DefaultRule;
        }

        if (options.RankDefaults.TryGetValue(rank, out var rankRule))
        {
            return rankRule;
        }

        return options.DefaultRule;
    }

    private void MaybePrune(ToolRateLimitingOptions options, long nowUnixSeconds)
    {
        if (options.PruneEveryNChecks <= 0)
        {
            return;
        }

        var checks = Interlocked.Increment(ref _checksSincePrune);
        if (checks < options.PruneEveryNChecks)
        {
            return;
        }

        Interlocked.Exchange(ref _checksSincePrune, 0);

        var idleSeconds = Math.Max(30, options.IdleEntryExpirationSeconds);
        var cutoff = nowUnixSeconds - idleSeconds;

        foreach (var kvp in _counters)
        {
            if (kvp.Value.LastSeenUnixSeconds != 0 && kvp.Value.LastSeenUnixSeconds < cutoff)
            {
                _counters.TryRemove(kvp.Key, out _);
            }
        }
    }
}
