
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Factory for creating and managing agents dynamically.
/// This interface typically bridges persona loading, dependency injection, and registry tracking.
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// Creates an agent instance from a persona.
    /// </summary>
    /// <param name="personaName">Persona name (usually maps to a JSON file in <c>souls/</c>).</param>
    /// <param name="rank">Rank to assign to the agent instance.</param>
    /// <param name="parentId">Optional parent agent id for hierarchical tracking.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IAgent> CreateAgentAsync(string personaName, AgentRank rank, string? parentId = null, CancellationToken ct = default);

    /// <summary>
    /// Creates an agent instance from an in-memory persona (no <c>souls/*.json</c> file required).
    /// This is used for dynamically specialized workers based on a generic persona template.
    /// </summary>
    /// <param name="persona">Persona to use for the agent instance (its <see cref="Persona.Name"/> is used as the agent display name).</param>
    /// <param name="rank">Rank to assign to the agent instance.</param>
    /// <param name="parentId">Optional parent agent id for hierarchical tracking.</param>
    /// <param name="personaPath">Optional persona identifier to store on the agent identity model (for observability/migration).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IAgent> CreateAgentAsync(Persona persona, AgentRank rank, string? parentId = null, string? personaPath = null, CancellationToken ct = default);

    /// <summary>
    /// Gets an active agent by id, if it exists.
    /// </summary>
    IAgent? GetAgent(string agentId);

    /// <summary>
    /// Gets all active agents.
    /// </summary>
    IEnumerable<IAgent> GetAllAgents();

    /// <summary>
    /// Registers an agent in the underlying registry.
    /// </summary>
    void RegisterAgent(IAgent agent);

    /// <summary>
    /// Unregisters an agent.
    /// </summary>
    void UnregisterAgent(string agentId);

    /// <summary>
    /// Terminates an agent and all its children (if any).
    /// Implementations should ensure child agents are stopped before removal.
    /// </summary>
    Task TerminateAgentAsync(string agentId, CancellationToken ct = default);
}
