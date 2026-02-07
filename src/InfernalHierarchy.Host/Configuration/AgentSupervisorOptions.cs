namespace InfernalHierarchy.Host.Configuration;

/// <summary>
/// Configuration for the Agent Supervisor background service.
/// The supervisor monitors agent activity and can request replans or preempt stuck agents.
/// </summary>
public sealed class AgentSupervisorOptions
{
    /// <summary>
    /// Enables or disables the supervisor loop.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Poll interval for supervisor checks.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A stall threshold after which a non-idle agent is considered stuck.
    /// </summary>
    public TimeSpan MaxStallDuration { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Maximum number of consecutive supervisor ticks with no observed progress before an agent is treated as looping.
    /// </summary>
    public int MaxNoProgressTicks { get; set; } = 8;

    /// <summary>
    /// Cooldown between interventions on the same agent.
    /// </summary>
    public TimeSpan InterventionCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// When true, the supervisor is allowed to preempt non-root agents.
    /// </summary>
    public bool PreemptEnabled { get; set; } = true;

    /// <summary>
    /// How many recent decisions to look back when inferring progress.
    /// </summary>
    public int DecisionLookbackCount { get; set; } = 50;
}
