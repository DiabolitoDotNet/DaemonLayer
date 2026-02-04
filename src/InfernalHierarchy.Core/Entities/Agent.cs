namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Represents a demon agent in the hierarchy
/// </summary>
public class Agent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public AgentRank Rank { get; set; }
    public string PersonaPath { get; set; } = string.Empty;
    public string? ParentAgentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public enum AgentRank
{
    Supreme,    // Lucifer/Belial - Main agent
    Prince,     // High-level coordinators
    Duke,       // Mid-level specialists
    Worker      // Task executors
}

public enum AgentStatus
{
    Idle,
    Thinking,
    ActingWithTool,
    Waiting,
    Suspended,  // Hibernated, can be resumed
    Terminated
}
