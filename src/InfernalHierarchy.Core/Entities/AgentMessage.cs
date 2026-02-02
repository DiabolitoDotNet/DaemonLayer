namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Message passed through the internal message bus
/// </summary>
public class AgentMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FromAgentId { get; set; } = string.Empty;
    public string? ToAgentId { get; set; } // null = broadcast
    public MessageType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object> Payload { get; init; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum MessageType
{
    Task,
    Report,
    Query,
    Command,
    Notification,
    ToolResult,
    Broadcast
}
