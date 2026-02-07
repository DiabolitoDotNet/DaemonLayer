
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Registry tracking all active agents in the hierarchy
/// </summary>
public interface IAgentRegistry
{
    /// <summary>
    /// Register an agent in the registry
    /// </summary>
    void Register(IAgent agent);

    /// <summary>
    /// Unregister an agent from the registry
    /// </summary>
    Task UnregisterAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Unregister an agent synchronously
    /// </summary>
    void Unregister(string agentId);

    /// <summary>
    /// Get an agent by ID
    /// </summary>
    IAgent? GetAgent(string agentId);

    /// <summary>
    /// Get all agents in the registry
    /// </summary>
    IEnumerable<IAgent> GetAllAgents();

    /// <summary>
    /// Get all agents of a specific rank
    /// </summary>
    IEnumerable<IAgent> GetAgentsByRank(AgentRank rank);

    /// <summary>
    /// Get all child agents of a specific parent
    /// </summary>
    IEnumerable<IAgent> GetChildAgents(string parentId);

    /// <summary>
    /// Get count of all agents
    /// </summary>
    int Count();

    /// <summary>
    /// Check if an agent is registered
    /// </summary>
    bool IsRegistered(string agentId);
}
