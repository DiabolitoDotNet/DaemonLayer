namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Rate limits tool execution requests (typically per-agent and per-tool) to prevent tool spamming.
/// </summary>
public interface IToolRateLimiter
{
    /// <summary>
    /// Evaluates whether the tool execution is allowed at this time.
    /// Implementations may consider agent id, tool name, tenant, and recent execution history.
    /// </summary>
    /// <param name="context">Tool execution context.</param>
    RateLimitDecision Check(ToolExecutionContext context);
}

/// <summary>
/// Rate limit decision result.
/// </summary>
/// <param name="Allowed">True if execution is allowed now.</param>
/// <param name="RetryAfter">Recommended wait time before retrying if denied.</param>
/// <param name="Reason">Optional denial reason for diagnostics.</param>
public readonly record struct RateLimitDecision(
    bool Allowed,
    TimeSpan RetryAfter,
    string? Reason = null)
{
    /// <summary>
    /// Allows execution immediately.
    /// </summary>
    public static RateLimitDecision Allow() => new(true, TimeSpan.Zero, null);

    /// <summary>
    /// Denies execution and provides a retry suggestion.
    /// </summary>
    public static RateLimitDecision Deny(TimeSpan retryAfter, string? reason = null) => new(false, retryAfter, reason);
}
