using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Base interface for all demon agents in the hierarchy
/// </summary>
public interface IAgent
{
    string Id { get; }
    string Name { get; }
    AgentRank Rank { get; }
    AgentStatus Status { get; }
    Persona Persona { get; }

    /// <summary>
    /// Start the agent's execution loop
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stop the agent gracefully
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Process a task message
    /// </summary>
    Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default);

    /// <summary>
    /// Check if this agent can create sub-agents of a specific rank
    /// </summary>
    bool CanCreateSubAgent(AgentRank targetRank);
}
