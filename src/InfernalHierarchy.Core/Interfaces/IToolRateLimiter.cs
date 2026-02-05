namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Rate limits tool execution requests (typically per-agent and per-tool) to prevent tool spamming.
/// </summary>
public interface IToolRateLimiter
{
    RateLimitDecision Check(ToolExecutionContext context);
}

public readonly record struct RateLimitDecision(
    bool Allowed,
    TimeSpan RetryAfter,
    string? Reason = null)
{
    public static RateLimitDecision Allow() => new(true, TimeSpan.Zero, null);

    public static RateLimitDecision Deny(TimeSpan retryAfter, string? reason = null) => new(false, retryAfter, reason);
}
