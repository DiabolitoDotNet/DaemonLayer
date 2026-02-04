using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace InfernalHierarchy.Agents;

/// <summary>
/// Registry tracking all active agents in the hierarchy
/// </summary>
public class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, IAgent> _agents = new();
    private readonly ConcurrentDictionary<string, DateTime> _agentCreationTimes = new();
    private readonly ILogger<AgentRegistry> _logger;

    public AgentRegistry(ILogger<AgentRegistry> logger)
    {
        _logger = logger;
    }

    public void Register(IAgent agent)
    {
        if (_agents.TryAdd(agent.Id, agent))
        {
            _agentCreationTimes[agent.Id] = DateTime.UtcNow;
            _logger.LogInformation("✅ Registered agent: {Name} ({Id}) - Rank: {Rank}",
                agent.Name, agent.Id, agent.Rank);
        }
        else
        {
            _logger.LogWarning("⚠️ Agent {Id} already registered", agent.Id);
        }
    }

    public async Task UnregisterAsync(string agentId, CancellationToken ct = default)
    {
        if (_agents.TryRemove(agentId, out var agent))
        {
            _agentCreationTimes.TryRemove(agentId, out _);

            try
            {
                // Gracefully stop the agent
                await agent.StopAsync(ct);
                _logger.LogInformation("✅ Unregistered and stopped agent: {Name} ({Id})", agent.Name, agentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error stopping agent {Name} during unregister", agent.Name);
            }
        }
        else
        {
            _logger.LogWarning("⚠️ Agent {Id} not found in registry", agentId);
        }
    }

    public void Unregister(string agentId)
    {
        if (_agents.TryRemove(agentId, out var agent))
        {
            _agentCreationTimes.TryRemove(agentId, out _);
            _logger.LogInformation("❌ Unregistered agent: {Name} ({Id})", agent.Name, agentId);
        }
    }

    public IAgent? GetAgent(string agentId)
    {
        _agents.TryGetValue(agentId, out var agent);
        return agent;
    }

    public IEnumerable<IAgent> GetAllAgents() => _agents.Values;

    public IEnumerable<IAgent> GetAgentsByRank(AgentRank rank)
        => _agents.Values.Where(a => a.Rank == rank);

    public IEnumerable<IAgent> GetChildAgents(string parentId)
        => _agents.Values.Where(a => a is BaseAgent ba && ba.ParentAgentId == parentId);

    public int Count() => _agents.Count;

    public bool IsRegistered(string agentId) => _agents.ContainsKey(agentId);

    public async Task TerminateAgentAsync(string agentId, CancellationToken ct = default)
    {
        var agent = GetAgent(agentId);
        if (agent == null)
        {
            _logger.LogWarning("⚠️ Cannot terminate agent {Id}: not found", agentId);
            return;
        }

        _logger.LogInformation("💀 Terminating agent: {Name} ({Id})", agent.Name, agentId);

        // Terminate all child agents first
        var children = GetChildAgents(agentId).ToList();
        foreach (var child in children)
        {
            await TerminateAgentAsync(child.Id, ct);
        }

        // Unregister and stop the agent
        await UnregisterAsync(agentId, ct);
    }

    public AgentStats GetStats()
    {
        var agents = _agents.Values.ToList();
        return new AgentStats
        {
            TotalAgents = agents.Count,
            SupremeCount = agents.Count(a => a.Rank == AgentRank.Supreme),
            PrinceCount = agents.Count(a => a.Rank == AgentRank.Prince),
            DukeCount = agents.Count(a => a.Rank == AgentRank.Duke),
            WorkerCount = agents.Count(a => a.Rank == AgentRank.Worker),
            IdleCount = agents.Count(a => a.Status == AgentStatus.Idle),
            ThinkingCount = agents.Count(a => a.Status == AgentStatus.Thinking),
            ActiveCount = agents.Count(a => a.Status == AgentStatus.ActingWithTool),
            OldestAgentAge = _agentCreationTimes.Values.Any()
                ? DateTime.UtcNow - _agentCreationTimes.Values.Min()
                : TimeSpan.Zero
        };
    }
}

public class AgentStats
{
    public int TotalAgents { get; set; }
    public int SupremeCount { get; set; }
    public int PrinceCount { get; set; }
    public int DukeCount { get; set; }
    public int WorkerCount { get; set; }
    public int IdleCount { get; set; }
    public int ThinkingCount { get; set; }
    public int ActiveCount { get; set; }
    public TimeSpan OldestAgentAge { get; set; }
}
