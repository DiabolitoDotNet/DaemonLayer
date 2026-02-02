using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Messaging;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Agents;

/// <summary>
/// Factory for creating and managing agents
/// </summary>
public class AgentFactory : IAgentFactory
{
    private readonly IPersonaLoader _personaLoader;
    private readonly IMessageBus _messageBus;
    private readonly ISharedMemory _sharedMemory;
    private readonly IToolRegistry _toolRegistry;
    private readonly AgentRegistry _registry;
    private readonly OllamaClient _ollamaClient;
    private readonly ILogger<AgentFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public AgentFactory(
        IPersonaLoader personaLoader,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        AgentRegistry registry,
        OllamaClient ollamaClient,
        ILogger<AgentFactory> logger,
        ILoggerFactory loggerFactory)
    {
        _personaLoader = personaLoader;
        _messageBus = messageBus;
        _sharedMemory = sharedMemory;
        _toolRegistry = toolRegistry;
        _registry = registry;
        _ollamaClient = ollamaClient;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<IAgent> CreateAgentAsync(string personaName, AgentRank rank, string? parentId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("🔨 Creating agent: {PersonaName} with rank {Rank}", personaName, rank);

        // Load persona
        var persona = await _personaLoader.LoadPersonaAsync(personaName, ct);
        if (persona == null)
        {
            throw new InvalidOperationException($"Persona '{personaName}' not found");
        }

        // Create agent entity
        var agentEntity = new Agent
        {
            Id = Guid.NewGuid().ToString(),
            Name = personaName,
            Rank = rank,
            ParentAgentId = parentId,
            PersonaPath = $"souls/{personaName.ToLower()}.json",
            Status = AgentStatus.Idle,
            CreatedAt = DateTime.UtcNow
        };

        // Create the concrete agent instance
        var agent = new ReActAgent(
            agentEntity,
            persona,
            _messageBus,
            _sharedMemory,
            _toolRegistry,
            this,
            _ollamaClient,
            _loggerFactory.CreateLogger<ReActAgent>());

        // Register it
        RegisterAgent(agent);

        return agent;
    }

    public IAgent? GetAgent(string agentId) => _registry.GetAgent(agentId);

    public IEnumerable<IAgent> GetAllAgents() => _registry.GetAllAgents();

    public void RegisterAgent(IAgent agent) => _registry.Register(agent);

    public void UnregisterAgent(string agentId) => _registry.Unregister(agentId);

    public async Task TerminateAgentAsync(string agentId, CancellationToken ct = default)
    {
        await _registry.TerminateAgentAsync(agentId, ct);

        // Cleanup message bus
        if (_messageBus is ChannelMessageBus messageBus)
        {
            messageBus.CleanupAgent(agentId);
        }
    }
}
