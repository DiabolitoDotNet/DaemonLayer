namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Represents an agent instance in the hierarchy.
/// This is the persisted/serializable identity model (distinct from runtime <c>IAgent</c> implementations).
/// </summary>
public class Agent
{
    /// <summary>
    /// Unique agent identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Agent rank used for delegation, policy, and persona selection.
    /// </summary>
    public AgentRank Rank { get; set; }

    /// <summary>
    /// Path or identifier for the persona backing this agent.
    /// </summary>
    public string PersonaPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional parent agent id to represent the hierarchy.
    /// </summary>
    public string? ParentAgentId { get; set; }

    /// <summary>
    /// UTC creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current status.
    /// </summary>
    public AgentStatus Status { get; set; } = AgentStatus.Idle;

    /// <summary>
    /// Additional extensible metadata (implementation-defined).
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Hierarchy rank for agents.
/// Higher ranks can typically delegate to or create lower-rank agents.
/// </summary>
public enum AgentRank
{
    Supreme,    // Lucifer/Belial - Main agent
    Prince,     // High-level coordinators
    Duke,       // Mid-level specialists
    Worker      // Task executors
}

/// <summary>
/// Lifecycle/status for an agent.
/// </summary>
public enum AgentStatus
{
    /// <summary>
    /// Not currently processing tasks.
    /// </summary>
    Idle,

    /// <summary>
    /// Reasoning/planning phase.
    /// </summary>
    Thinking,

    /// <summary>
    /// Currently executing a tool.
    /// </summary>
    ActingWithTool,

    /// <summary>
    /// Waiting on external input or async completion.
    /// </summary>
    Waiting,

    /// <summary>
    /// Suspended/hibernated.
    /// </summary>
    Suspended,  // Hibernated, can be resumed

    /// <summary>
    /// Permanently terminated.
    /// </summary>
    Terminated
}
