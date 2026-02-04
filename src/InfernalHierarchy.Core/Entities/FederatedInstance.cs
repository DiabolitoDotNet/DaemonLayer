namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Represents a federated InfernalHierarchy instance
/// </summary>
public class FederatedInstance
{
    /// <summary>
    /// Gets or sets the unique instance identifier
    /// </summary>
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the instance name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base URL for this instance
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API key for authentication
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets whether this instance is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the last heartbeat timestamp
    /// </summary>
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the instance capabilities
    /// </summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets the current load (0.0-1.0)
    /// </summary>
    public double CurrentLoad { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the maximum number of agents
    /// </summary>
    public int MaxAgents { get; set; } = 100;

    /// <summary>
    /// Gets or sets current agent count
    /// </summary>
    public int CurrentAgentCount { get; set; } = 0;
}

/// <summary>
/// Message for cross-instance communication
/// </summary>
public class FederatedMessage
{
    /// <summary>
    /// Gets or sets the message identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets source instance ID
    /// </summary>
    public string SourceInstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets target instance ID
    /// </summary>
    public string TargetInstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets message type
    /// </summary>
    public FederatedMessageType MessageType { get; set; }

    /// <summary>
    /// Gets or sets message payload
    /// </summary>
    public Dictionary<string, object> Payload { get; set; } = new();

    /// <summary>
    /// Gets or sets timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets time-to-live in seconds
    /// </summary>
    public int TtlSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets whether response is required
    /// </summary>
    public bool RequiresResponse { get; set; } = false;

    /// <summary>
    /// Gets or sets correlation ID for request/response pairing
    /// </summary>
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Types of federated messages
/// </summary>
public enum FederatedMessageType
{
    /// <summary>
    /// Heartbeat to check instance health
    /// </summary>
    Heartbeat = 0,

    /// <summary>
    /// Request to create agent on remote instance
    /// </summary>
    CreateAgent = 1,

    /// <summary>
    /// Delegate task to remote agent
    /// </summary>
    DelegateTask = 2,

    /// <summary>
    /// Query remote memory
    /// </summary>
    MemoryQuery = 3,

    /// <summary>
    /// Synchronize shared memory
    /// </summary>
    MemorySync = 4,

    /// <summary>
    /// Request collaboration across instances
    /// </summary>
    CollaborationRequest = 5,

    /// <summary>
    /// Load balancing request
    /// </summary>
    LoadBalance = 6,

    /// <summary>
    /// Broadcast event to all instances
    /// </summary>
    Broadcast = 7
}
