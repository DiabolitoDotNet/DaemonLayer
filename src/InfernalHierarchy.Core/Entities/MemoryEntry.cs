namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Memory entries stored in LiteDB
/// </summary>
public abstract class MemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty; // Agent ID
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
