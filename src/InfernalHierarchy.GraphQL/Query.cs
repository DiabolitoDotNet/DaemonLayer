using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.GraphQL;

/// <summary>
/// GraphQL query resolver for InfernalHierarchy
/// </summary>
public class Query
{
    /// <summary>
    /// Gets all active agents
    /// </summary>
    /// <param name="registry">Agent registry</param>
    /// <returns>List of agents</returns>
    public List<Agent> GetAgents([Service] IAgentRegistry registry)
    {
        return registry.GetAll();
    }

    /// <summary>
    /// Gets agent by ID
    /// </summary>
    /// <param name="id">Agent identifier</param>
    /// <param name="registry">Agent registry</param>
    /// <returns>Agent or null</returns>
    public Agent? GetAgent(string id, [Service] IAgentRegistry registry)
    {
        return registry.GetById(id);
    }

    /// <summary>
    /// Gets agents by rank
    /// </summary>
    /// <param name="rank">Agent rank</param>
    /// <param name="registry">Agent registry</param>
    /// <returns>List of agents</returns>
    public List<Agent> GetAgentsByRank(AgentRank rank, [Service] IAgentRegistry registry)
    {
        return registry.GetByRank(rank);
    }

    /// <summary>
    /// Gets agent hierarchy (parent-child relationships)
    /// </summary>
    /// <param name="rootAgentId">Root agent ID (null for supreme agent)</param>
    /// <param name="registry">Agent registry</param>
    /// <returns>Hierarchical agent structure</returns>
    public AgentHierarchy GetAgentHierarchy(string? rootAgentId, [Service] IAgentRegistry registry)
    {
        var root = rootAgentId != null 
            ? registry.GetById(rootAgentId) 
            : registry.GetByRank(AgentRank.Supreme).FirstOrDefault();

        if (root == null)
        {
            return new AgentHierarchy { Agent = null, Children = new List<AgentHierarchy>() };
        }

        return BuildHierarchy(root, registry);
    }

    /// <summary>
    /// Search facts in shared memory
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="confidence">Minimum confidence threshold</param>
    /// <param name="memory">Shared memory service</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of facts</returns>
    public async Task<List<Fact>> SearchFacts(
        string query, 
        double? confidence,
        [Service] ISharedMemory memory,
        CancellationToken ct)
    {
        return await memory.SearchFactsAsync(query, confidence ?? 0.0, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Get all decisions
    /// </summary>
    /// <param name="agentId">Filter by agent ID (optional)</param>
    /// <param name="memory">Shared memory service</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of decisions</returns>
    public async Task<List<Decision>> GetDecisions(
        string? agentId,
        [Service] ISharedMemory memory,
        CancellationToken ct)
    {
        var all = await memory.GetAllDecisionsAsync(ct).ConfigureAwait(false);
        
        if (agentId != null)
        {
            return all.Where(d => d.MadeBy == agentId).ToList();
        }

        return all;
    }

    /// <summary>
    /// Get all tasks
    /// </summary>
    /// <param name="status">Filter by status (optional)</param>
    /// <param name="memory">Shared memory service</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of tasks</returns>
    public async Task<List<AgentTask>> GetTasks(
        TaskStatus? status,
        [Service] ISharedMemory memory,
        CancellationToken ct)
    {
        var all = await memory.GetAllTasksAsync(ct).ConfigureAwait(false);
        
        if (status.HasValue)
        {
            return all.Where(t => t.Status == status.Value).ToList();
        }

        return all;
    }

    /// <summary>
    /// Get collaboration history
    /// </summary>
    /// <param name="agentId">Filter by agent ID (optional)</param>
    /// <param name="collaborationService">Collaboration service</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of collaboration requests</returns>
    public async Task<List<CollaborationRequest>> GetCollaborations(
        string? agentId,
        [Service] IAgentCollaborationService collaborationService,
        CancellationToken ct)
    {
        return await collaborationService.GetCollaborationHistoryAsync(agentId, ct).ConfigureAwait(false);
    }

    private static AgentHierarchy BuildHierarchy(Agent agent, IAgentRegistry registry)
    {
        var children = registry.GetChildren(agent.Id)
            .Select(child => BuildHierarchy(child, registry))
            .ToList();

        return new AgentHierarchy
        {
            Agent = agent,
            Children = children
        };
    }
}

/// <summary>
/// Represents hierarchical agent structure for GraphQL
/// </summary>
public class AgentHierarchy
{
    /// <summary>
    /// Gets or sets the agent
    /// </summary>
    public Agent? Agent { get; set; }

    /// <summary>
    /// Gets or sets child agents
    /// </summary>
    public List<AgentHierarchy> Children { get; set; } = new();
}
