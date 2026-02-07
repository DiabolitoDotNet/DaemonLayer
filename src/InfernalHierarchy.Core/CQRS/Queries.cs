
namespace InfernalHierarchy.Core.CQRS;

/// <summary>
/// Base interface for all queries
/// </summary>
/// <typeparam name="TResult">Query result type</typeparam>
public interface IQuery<TResult>
{
    /// <summary>
    /// Gets the query identifier
    /// </summary>
    string QueryId { get; }
}

/// <summary>
/// Base interface for all query handlers
/// </summary>
/// <typeparam name="TQuery">Query type</typeparam>
/// <typeparam name="TResult">Result type</typeparam>
public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    /// <summary>
    /// Handles the query
    /// </summary>
    /// <param name="query">Query to handle</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Query result</returns>
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default);
}

/// <summary>
/// Query to get agent by ID
/// </summary>
public record GetAgentByIdQuery : IQuery<Agent?>
{
    /// <inheritdoc/>
    public string QueryId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Agent ID to retrieve
    /// </summary>
    public required string AgentId { get; init; }
}

/// <summary>
/// Query to get all agents by rank
/// </summary>
public record GetAgentsByRankQuery : IQuery<List<Agent>>
{
    /// <inheritdoc/>
    public string QueryId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Agent rank filter
    /// </summary>
    public required AgentRank Rank { get; init; }
}

/// <summary>
/// Query to get agent hierarchy
/// </summary>
public record GetAgentHierarchyQuery : IQuery<AgentHierarchyResult>
{
    /// <inheritdoc/>
    public string QueryId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Root agent ID (null for supreme agent)
    /// </summary>
    public string? RootAgentId { get; init; }
}

/// <summary>
/// Agent hierarchy result
/// </summary>
public class AgentHierarchyResult
{
    /// <summary>
    /// Root agent
    /// </summary>
    public Agent? Root { get; set; }

    /// <summary>
    /// All agents in hierarchy
    /// </summary>
    public List<Agent> AllAgents { get; set; } = new();

    /// <summary>
    /// Parent-child relationships
    /// </summary>
    public Dictionary<string, List<string>> Relationships { get; set; } = new();
}

/// <summary>
/// Query to search facts
/// </summary>
public record SearchFactsQuery : IQuery<List<Fact>>
{
    /// <inheritdoc/>
    public string QueryId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Search query text
    /// </summary>
    public required string SearchText { get; init; }

    /// <summary>
    /// Minimum confidence
    /// </summary>
    public double MinimumConfidence { get; init; } = 0.0;

    /// <summary>
    /// Tags to filter by
    /// </summary>
    public List<string>? Tags { get; init; }
}

/// <summary>
/// Query to get decisions by agent
/// </summary>
public record GetDecisionsByAgentQuery : IQuery<List<Decision>>
{
    /// <inheritdoc/>
    public string QueryId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Time range start
    /// </summary>
    public DateTime? StartTime { get; init; }

    /// <summary>
    /// Time range end
    /// </summary>
    public DateTime? EndTime { get; init; }
}

/// <summary>
/// Query to get tasks by status
/// </summary>
public record GetTasksByStatusQuery : IQuery<List<Entities.TaskEntry>>
{
    /// <inheritdoc/>
    public string QueryId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Task status filter
    /// </summary>
    public Entities.TaskStatus Status { get; init; }

    /// <summary>
    /// Assigned agent filter
    /// </summary>
    public string? AssignedTo { get; init; }
}

/// <summary>
/// Query to get collaboration history
/// </summary>
public record GetCollaborationHistoryQuery : IQuery<List<CollaborationRequest>>
{
    /// <inheritdoc/>
    public string QueryId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Agent ID filter
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// Strategy filter
    /// </summary>
    public CollaborationStrategy? Strategy { get; init; }

    /// <summary>
    /// Status filter
    /// </summary>
    public CollaborationStatus? Status { get; init; }
}

/// <summary>
/// Query to get agent statistics
/// </summary>
public record GetAgentStatisticsQuery : IQuery<AgentStatistics>
{
    /// <inheritdoc/>
    public string QueryId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }
}

/// <summary>
/// Agent statistics result
/// </summary>
public class AgentStatistics
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Total tasks completed
    /// </summary>
    public int TasksCompleted { get; set; }

    /// <summary>
    /// Total decisions made
    /// </summary>
    public int DecisionsMade { get; set; }

    /// <summary>
    /// Total tool executions
    /// </summary>
    public int ToolExecutions { get; set; }

    /// <summary>
    /// Average decision confidence
    /// </summary>
    public double AverageConfidence { get; set; }

    /// <summary>
    /// Child agent count
    /// </summary>
    public int ChildAgentCount { get; set; }

    /// <summary>
    /// Average task completion time (milliseconds)
    /// </summary>
    public double AverageTaskCompletionMs { get; set; }
}
