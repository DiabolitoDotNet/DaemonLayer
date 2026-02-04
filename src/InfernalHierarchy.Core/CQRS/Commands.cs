using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core.CQRS;

/// <summary>
/// Base interface for all commands
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Gets the command identifier
    /// </summary>
    string CommandId { get; }

    /// <summary>
    /// Gets the timestamp when command was created
    /// </summary>
    DateTime Timestamp { get; }
}

/// <summary>
/// Base interface for all command handlers
/// </summary>
/// <typeparam name="TCommand">Command type</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>
    /// Handles the command
    /// </summary>
    /// <param name="command">Command to handle</param>
    /// <param name="ct">Cancellation token</param>
    Task HandleAsync(TCommand command, CancellationToken ct = default);
}

/// <summary>
/// Command to create a new agent
/// </summary>
public record CreateAgentCommand : ICommand
{
    /// <inheritdoc/>
    public string CommandId { get; init; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Persona name for the agent
    /// </summary>
    public required string PersonaName { get; init; }

    /// <summary>
    /// Parent agent ID
    /// </summary>
    public string? ParentAgentId { get; init; }

    /// <summary>
    /// Agent name override
    /// </summary>
    public string? Name { get; init; }
}

/// <summary>
/// Command to terminate an agent
/// </summary>
public record TerminateAgentCommand : ICommand
{
    /// <inheritdoc/>
    public string CommandId { get; init; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Agent ID to terminate
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Termination reason
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Command to add a fact to memory
/// </summary>
public record AddFactCommand : ICommand
{
    /// <inheritdoc/>
    public string CommandId { get; init; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Fact content
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Fact source
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Confidence level
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>
    /// Tags
    /// </summary>
    public List<string> Tags { get; init; } = new();
}

/// <summary>
/// Command to request agent collaboration
/// </summary>
public record RequestCollaborationCommand : ICommand
{
    /// <inheritdoc/>
    public string CommandId { get; init; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Initiating agent ID
    /// </summary>
    public required string InitiatorAgentId { get; init; }

    /// <summary>
    /// Collaboration task
    /// </summary>
    public required string Task { get; init; }

    /// <summary>
    /// Collaboration strategy
    /// </summary>
    public CollaborationStrategy Strategy { get; init; } = CollaborationStrategy.Voting;

    /// <summary>
    /// Participant agent IDs
    /// </summary>
    public List<string> ParticipantAgentIds { get; init; } = new();

    /// <summary>
    /// Minimum confidence threshold
    /// </summary>
    public double MinimumConfidence { get; init; } = 0.7;

    /// <summary>
    /// Timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Command to execute a tool
/// </summary>
public record ExecuteToolCommand : ICommand
{
    /// <inheritdoc/>
    public string CommandId { get; init; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Agent executing the tool
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Tool name
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Tool parameters
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = new();
}
