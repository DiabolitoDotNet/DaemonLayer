namespace InfernalHierarchy.Core.Configuration;

public enum MessageQueueOverflowPolicy
{
    Block = 0,
    DropOldest = 1,
    Reject = 2
}

public sealed class MessageBusOptions
{
    public int QueueCapacity { get; set; } = 1000;

    public MessageQueueOverflowPolicy OverflowPolicy { get; set; } = MessageQueueOverflowPolicy.Block;

    public MessageBusBackpressureOptions Backpressure { get; set; } = new();
}

public sealed class MessageBusBackpressureOptions
{
    /// <summary>
    /// Enables adaptive backpressure behavior.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Activates backpressure mode when total queue depth reaches this ratio of capacity.
    /// </summary>
    public double HighWatermarkRatio { get; set; } = 0.85;

    /// <summary>
    /// Deactivates backpressure mode when total queue depth falls to this ratio of capacity.
    /// </summary>
    public double RecoverWatermarkRatio { get; set; } = 0.55;

    /// <summary>
    /// When true, collaboration requests are selectively deferred while backpressure is active.
    /// </summary>
    public bool DeferCollaborationRequests { get; set; } = true;

    /// <summary>
    /// When true, selected tool executions are temporarily deferred while backpressure is active.
    /// </summary>
    public bool DeferToolExecutions { get; set; } = true;

    /// <summary>
    /// Suggested retry interval for deferred tool executions.
    /// </summary>
    public int ToolRetryAfterMs { get; set; } = 1500;

    /// <summary>
    /// Tool names to defer while backpressure is active.
    /// </summary>
    public string[] DeferredToolNames { get; set; } =
    [
        "request_collaboration",
        "create_sub_agent",
        "send_agent_message"
    ];
}