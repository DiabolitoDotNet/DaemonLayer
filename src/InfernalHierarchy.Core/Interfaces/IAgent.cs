
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Base interface for all agents in the hierarchy.
/// Agents encapsulate persona-driven behavior, can process tasks, and may delegate to sub-agents depending on rank.
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Unique identifier for the agent instance.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name of the agent.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Hierarchical rank used for delegation rules and tool permissions.
    /// </summary>
    AgentRank Rank { get; }

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    AgentStatus Status { get; }

    /// <summary>
    /// Persona that drives behavior (system prompt, available tools, specializations).
    /// </summary>
    Persona Persona { get; }

    /// <summary>
    /// Starts the agent's execution loop.
    /// Implementations should be idempotent (subsequent calls do not create multiple loops).
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the agent gracefully.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Suspends the agent (hibernation): pauses execution and preserves any required state.
    /// </summary>
    Task SuspendAsync(CancellationToken ct = default);

    /// <summary>
    /// Resumes the agent from suspension.
    /// </summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>
    /// Processes a task message and returns a response message.
    /// Implementations should treat <paramref name="task"/> as immutable input and return a new message.
    /// </summary>
    Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default);

    /// <summary>
    /// Determines whether this agent is allowed to create sub-agents of the specified rank.
    /// </summary>
    bool CanCreateSubAgent(AgentRank targetRank);
}
