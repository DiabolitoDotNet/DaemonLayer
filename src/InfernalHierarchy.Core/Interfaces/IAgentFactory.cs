
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Factory for creating agents dynamically
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// Create an agent from a persona
    /// </summary>
    Task<IAgent> CreateAgentAsync(string personaName, AgentRank rank, string? parentId = null, CancellationToken ct = default);

    /// <summary>
    /// Get an agent by ID
    /// </summary>
    IAgent? GetAgent(string agentId);

    /// <summary>
    /// Get all active agents
    /// </summary>
    IEnumerable<IAgent> GetAllAgents();

    /// <summary>
    /// Register an agent in the registry
    /// </summary>
    void RegisterAgent(IAgent agent);

    /// <summary>
    /// Unregister an agent
    /// </summary>
    void UnregisterAgent(string agentId);

    /// <summary>
    /// Terminate an agent and all its children
    /// </summary>
    Task TerminateAgentAsync(string agentId, CancellationToken ct = default);
}
