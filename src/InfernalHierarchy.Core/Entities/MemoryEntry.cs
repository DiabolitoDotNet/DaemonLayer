namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Base class for memory entries stored in shared memory.
/// Implementations should treat derived types as durable records that may be shared across agents.
/// </summary>
public abstract class MemoryEntry
{
    /// <summary>
    /// Unique identifier for this entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// UTC creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creating agent id.
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty; // Agent ID
    
    // Cross-agent memory sharing

    /// <summary>
    /// Visibility model for this entry.
    /// </summary>
    public MemoryVisibility Visibility { get; set; } = MemoryVisibility.Private;

    /// <summary>
    /// Explicit list of agent ids allowed to view this entry when <see cref="Visibility"/> is <see cref="MemoryVisibility.Shared"/>.
    /// </summary>
    public List<string> SharedWithAgents { get; set; } = new(); // Specific agent IDs

    /// <summary>
    /// Minimum rank required to view this entry when <see cref="Visibility"/> is <see cref="MemoryVisibility.RankBased"/>.
    /// </summary>
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
    /// <summary>
    /// Decision context/problem statement.
    /// </summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// Action chosen.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Reasoning/justification for the action.
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// Optional outcome/result after execution.
    /// </summary>
    public string? Outcome { get; set; }
}

public class Fact : MemoryEntry
{
    /// <summary>
    /// Category used for filtering/grouping.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Fact content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Source attribution (url, document id, operator note, etc.).
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score in the fact (0..1).
    /// </summary>
    public double Confidence { get; set; } = 1.0;
    
    // Version tracking

    /// <summary>
    /// Current version number (starts at 1).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Optional id of the previous version record (implementation-defined).
    /// </summary>
    public string? PreviousVersionId { get; set; }

    /// <summary>
    /// UTC timestamp of the last modification.
    /// </summary>
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Agent id that last modified the fact.
    /// </summary>
    public string LastModifiedBy { get; set; } = string.Empty;

    /// <summary>
    /// Inlined history of versions.
    /// </summary>
    public List<FactVersion> VersionHistory { get; set; } = new();
}

/// <summary>
/// Tracks historical versions of facts
/// </summary>
public class FactVersion
{
    /// <summary>
    /// Version number of this historical record.
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// Content for this version.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score for this version.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// UTC timestamp of modification.
    /// </summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>
    /// Agent id that modified the fact.
    /// </summary>
    public string ModifiedBy { get; set; } = string.Empty;

    /// <summary>
    /// Reason for the change.
    /// </summary>
    public string ChangeReason { get; set; } = string.Empty;
}

public class TaskEntry : MemoryEntry
{
    /// <summary>
    /// Task description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Agent id the task is assigned to.
    /// </summary>
    public string AssignedTo { get; set; } = string.Empty;

    /// <summary>
    /// Current task status.
    /// </summary>
    public TaskStatus Status { get; set; } = TaskStatus.Pending;

    /// <summary>
    /// Optional result text once completed/failed.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// UTC completion timestamp, if completed.
    /// </summary>
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
