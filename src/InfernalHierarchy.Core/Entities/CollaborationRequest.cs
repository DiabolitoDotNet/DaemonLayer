namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Represents a multi-agent collaboration request for consensus decision-making
/// </summary>
public class CollaborationRequest
{
    /// <summary>
    /// Gets or sets unique identifier for the collaboration request
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the originating agent ID
    /// </summary>
    public string InitiatorAgentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the task description for collaboration
    /// </summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collaboration strategy to use
    /// </summary>
    public CollaborationStrategy Strategy { get; set; } = CollaborationStrategy.Voting;

    /// <summary>
    /// Gets or sets minimum confidence threshold (0.0-1.0)
    /// </summary>
    public double MinimumConfidence { get; set; } = 0.7;

    /// <summary>
    /// Gets or sets minimum number of participating agents
    /// </summary>
    public int MinimumParticipants { get; set; } = 2;

    /// <summary>
    /// Gets or sets maximum time to wait for responses
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets when the request was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets participating agent IDs
    /// </summary>
    public List<string> ParticipantAgentIds { get; set; } = new();

    /// <summary>
    /// Gets or sets current status
    /// </summary>
    public CollaborationStatus Status { get; set; } = CollaborationStatus.Pending;

    /// <summary>
    /// Gets or sets when the request was completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the final decision result
    /// </summary>
    public CollaborationResult? Result { get; set; }
}

/// <summary>
/// Strategy for multi-agent collaboration
/// </summary>
public enum CollaborationStrategy
{
    /// <summary>
    /// Simple majority voting
    /// </summary>
    Voting = 0,

    /// <summary>
    /// Weighted voting based on agent rank and expertise
    /// </summary>
    WeightedVoting = 1,

    /// <summary>
    /// All agents must agree
    /// </summary>
    Consensus = 2,

    /// <summary>
    /// Use highest confidence response
    /// </summary>
    HighestConfidence = 3,

    /// <summary>
    /// Hierarchical decision (higher ranks override)
    /// </summary>
    Hierarchical = 4
}

/// <summary>
/// Status of collaboration request
/// </summary>
public enum CollaborationStatus
{
    /// <summary>
    /// Request created, waiting for participants
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Agents are processing the request
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// Consensus reached successfully
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Request timed out
    /// </summary>
    TimedOut = 3,

    /// <summary>
    /// Failed to reach consensus
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Request was cancelled
    /// </summary>
    Cancelled = 5
}

/// <summary>
/// Response from a single agent in collaboration
/// </summary>
public class AgentResponse
{
    /// <summary>
    /// Gets or sets the responding agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the agent's rank
    /// </summary>
    public AgentRank AgentRank { get; set; } = AgentRank.Worker;

    /// <summary>
    /// Gets or sets the agent's decision/response
    /// </summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets confidence score (0.0-1.0)
    /// </summary>
    public double Confidence { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the reasoning behind the decision
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the response was received
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets processing time in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Gets or sets weight for weighted voting (calculated based on rank, confidence, expertise)
    /// </summary>
    public double Weight { get; set; } = 1.0;
}

/// <summary>
/// Result of collaboration decision-making
/// </summary>
public class CollaborationResult
{
    /// <summary>
    /// Gets or sets the final aggregated decision
    /// </summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets overall confidence in the decision (0.0-1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets all agent responses
    /// </summary>
    public List<AgentResponse> Responses { get; set; } = new();

    /// <summary>
    /// Gets or sets aggregated reasoning from all agents
    /// </summary>
    public string AggregatedReasoning { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets number of agents that participated
    /// </summary>
    public int ParticipantCount { get; set; }

    /// <summary>
    /// Gets or sets agreement percentage (0.0-1.0)
    /// </summary>
    public double AgreementScore { get; set; }

    /// <summary>
    /// Gets or sets strategy used for decision
    /// </summary>
    public CollaborationStrategy Strategy { get; set; }

    /// <summary>
    /// Gets or sets winning response (if applicable)
    /// </summary>
    public AgentResponse? WinningResponse { get; set; }
}
