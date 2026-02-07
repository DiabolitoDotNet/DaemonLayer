namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Message passed through the internal message bus.
/// This is the primary envelope for inter-agent and host-to-agent communication.
/// </summary>
public class AgentMessage
{
    /// <summary>
    /// Unique message identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Sender agent identifier.
    /// For host-originated messages, this may be a synthetic id.
    /// </summary>
    public string FromAgentId { get; set; } = string.Empty;

    /// <summary>
    /// Target agent identifier. When null, the message is considered a broadcast.
    /// </summary>
    public string? ToAgentId { get; set; } // null = broadcast

    /// <summary>
    /// Message type used for routing and interpretation.
    /// </summary>
    public MessageType Type { get; set; }

    /// <summary>
    /// Primary human-readable content of the message.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional structured payload for tool results, task parameters, etc.
    /// </summary>
    public Dictionary<string, object> Payload { get; init; } = new();

    /// <summary>
    /// UTC timestamp set at creation time.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Supported message categories.
/// </summary>
public enum MessageType
{
    /// <summary>
    /// A unit of work to be performed by an agent.
    /// </summary>
    Task,

    /// <summary>
    /// A report back to a coordinator/parent agent.
    /// </summary>
    Report,

    /// <summary>
    /// A request for information.
    /// </summary>
    Query,

    /// <summary>
    /// A command to change state or behavior.
    /// </summary>
    Command,

    /// <summary>
    /// General notification message.
    /// </summary>
    Notification,

    /// <summary>
    /// A tool execution result.
    /// </summary>
    ToolResult,

    /// <summary>
    /// Broadcast message (often paired with <see cref="AgentMessage.ToAgentId"/> being null).
    /// </summary>
    Broadcast,

    /// <summary>
    /// Collaboration request / consensus workflow.
    /// </summary>
    CollaborationRequest
}
