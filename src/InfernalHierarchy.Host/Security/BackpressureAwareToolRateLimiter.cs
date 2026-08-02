using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Messaging.Bus;
using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Host.Security;

internal sealed class BackpressureAwareToolRateLimiter : IToolRateLimiter
{
    private readonly IMessageBus _messageBus;
    private readonly FixedWindowToolRateLimiter _innerLimiter;
    private readonly IOptions<MessageBusOptions> _messageBusOptions;
    private readonly MetricsCollector? _metrics;
    private readonly ILogger<BackpressureAwareToolRateLimiter> _logger;

    public BackpressureAwareToolRateLimiter(
        IMessageBus messageBus,
        FixedWindowToolRateLimiter innerLimiter,
        IOptions<MessageBusOptions> messageBusOptions,
        ILogger<BackpressureAwareToolRateLimiter> logger,
        MetricsCollector? metrics = null)
    {
        _messageBus = messageBus;
        _innerLimiter = innerLimiter;
        _messageBusOptions = messageBusOptions;
        _logger = logger;
        _metrics = metrics;
    }

    public RateLimitDecision Check(ToolExecutionContext context)
    {
        var innerDecision = _innerLimiter.Check(context);
        if (!innerDecision.Allowed)
        {
            return innerDecision;
        }

        if (_messageBus is not ChannelMessageBus bus)
        {
            return innerDecision;
        }

        var options = _messageBusOptions.Value.Backpressure ?? new MessageBusBackpressureOptions();
        if (!options.Enabled || !options.DeferToolExecutions || !bus.IsBackpressureActive)
        {
            return innerDecision;
        }

        var toolName = context.ToolName ?? string.Empty;
        var shouldDefer = options.DeferredToolNames
            .Any(t => string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase));

        if (!shouldDefer)
        {
            return innerDecision;
        }

        _metrics?.IncrementCounter("message_bus.backpressure.tool_deferrals");
        var retryAfterMs = Math.Max(100, options.ToolRetryAfterMs);

        _logger.LogWarning(
            "Deferring tool execution due to active backpressure | tool={Tool} retry_after_ms={RetryAfterMs}",
            toolName,
            retryAfterMs);

        return RateLimitDecision.Deny(
            retryAfter: TimeSpan.FromMilliseconds(retryAfterMs),
            reason: "Execution deferred due to active message-bus backpressure");
    }
}