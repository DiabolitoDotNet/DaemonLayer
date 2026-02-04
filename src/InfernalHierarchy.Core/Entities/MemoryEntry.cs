namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Memory entries stored in LiteDB
/// </summary>
public abstract class MemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty; // Agent ID
    
    // Cross-agent memory sharing
    public MemoryVisibility Visibility { get; set; } = MemoryVisibility.Private;
    public List<string> SharedWithAgents { get; set; } = new(); // Specific agent IDs
    public AgentRank? MinimumRankToView { get; set; } // Minimum rank required
}

/// <summary>
/// Visibility level for memory entries
/// </summary>
public enum MemoryVisibility
{
    /// <summary>
    /// Only visible to the creating agent
    /// </summary>
    Private = 0,
    
    /// <summary>
    /// Visible to agents of specified rank and above
    /// </summary>
    RankBased = 1,
    
    /// <summary>
    /// Visible to specific agents
    /// </summary>
    Shared = 2,
    
    /// <summary>
    /// Visible to all agents in hierarchy
    /// </summary>
    Public = 3
}

public class Decision : MemoryEntry
{
    public string Context { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public string? Outcome { get; set; }
}

public class Fact : MemoryEntry
{
    public string Category { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
    
    // Version tracking
    public int Version { get; set; } = 1;
    public string? PreviousVersionId { get; set; }
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    public string LastModifiedBy { get; set; } = string.Empty;
    public List<FactVersion> VersionHistory { get; set; } = new();
}

/// <summary>
/// Tracks historical versions of facts
/// </summary>
public class FactVersion
{
    public int VersionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
}

public class TaskEntry : MemoryEntry
{
    public string Description { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public string? Result { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum TaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}
