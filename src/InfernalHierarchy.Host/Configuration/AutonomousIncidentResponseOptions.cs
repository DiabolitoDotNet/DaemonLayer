namespace InfernalHierarchy.Host.Configuration;

/// <summary>
/// Configures autonomous mitigation loops for common runtime incident patterns.
/// </summary>
public sealed class AutonomousIncidentResponseOptions
{
    /// <summary>
    /// Enables or disables the autonomous incident response loop.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Polling interval for evaluating incident signals.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Cooldown between two mitigation actions.
    /// </summary>
    public TimeSpan ActionCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Trigger threshold for tool timeout growth between two polling cycles.
    /// </summary>
    public long ToolTimeoutSpikeThreshold { get; set; } = 3;

    /// <summary>
    /// Trigger threshold for queue reject growth between two polling cycles.
    /// </summary>
    public long QueueRejectGrowthThreshold { get; set; } = 5;

    /// <summary>
    /// Trigger threshold for detected stalled branches between two polling cycles.
    /// </summary>
    public long StalledBranchDetectionThreshold { get; set; } = 2;

    /// <summary>
    /// Trigger threshold for detected looping branches between two polling cycles.
    /// </summary>
    public long LoopingBranchDetectionThreshold { get; set; } = 2;

    /// <summary>
    /// Root agent targeted for global replans.
    /// </summary>
    public string RootAgentId { get; set; } = "lucifer";

    /// <summary>
    /// When true, the service can preempt one non-root active branch on looping spikes.
    /// </summary>
    public bool EnableBranchPreemption { get; set; } = true;

    /// <summary>
    /// When true, temporary tool-rate reduction is applied for severe incidents.
    /// </summary>
    public bool EnableTemporaryRateReduction { get; set; } = true;

    /// <summary>
    /// Duration of temporary rate reduction window.
    /// </summary>
    public TimeSpan RateReductionDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Retry-after value returned to deferred tools while rate reduction is active.
    /// </summary>
    public int RateReductionRetryAfterMs { get; set; } = 2000;

    /// <summary>
    /// Tool names deferred while temporary rate reduction is active.
    /// </summary>
    public string[] DeferredToolNames { get; set; } =
    [
        "request_collaboration",
        "create_sub_agent",
        "send_agent_message"
    ];

    /// <summary>
    /// Emits explicit mitigation events when true.
    /// </summary>
    public bool EmitAuditEvents { get; set; } = true;
}