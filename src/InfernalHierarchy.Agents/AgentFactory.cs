using InfernalHierarchy.Agents.Registry;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Tools;
using InfernalHierarchy.Core.Utilities;

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
    private readonly ILlmClient _ollamaClient;
    private readonly ILogger<AgentFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAgentEventSink? _eventSink;
    private readonly IVectorMemory? _vectorMemory;
    private readonly RagOptions _ragOptions;
    private readonly ReActOptions _reActOptions;
    private readonly CritiqueOptions _critiqueOptions;
    private readonly TokenUsageTracker? _tokenUsageTracker;
    private readonly MultiModelLlmClient? _multiModelLlmClient;
    private readonly IAgentCollaborationService? _collaborationService;
    private readonly IActionParser? _actionParser;
    private readonly IActionInputParser? _actionInputParser;
    private readonly IActionExecutor? _actionExecutor;
    private readonly IReportGenerator? _reportGenerator;
    private readonly IReActPromptBuilder? _promptBuilder;
    private readonly IReActLoopRunner? _loopRunner;
    private readonly IReActTaskProcessor? _taskProcessor;

    public AgentFactory(
        IPersonaLoader personaLoader,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        AgentRegistry registry,
        ILlmClient ollamaClient,
        ILogger<AgentFactory> logger,
        ILoggerFactory loggerFactory,
        IVectorMemory vectorMemory,
        IOptions<RagOptions> ragOptions,
        IOptions<ReActOptions> reActOptions,
        IAgentEventSink? eventSink,
        IActionParser? actionParser = null,
        IActionInputParser? actionInputParser = null,
        IActionExecutor? actionExecutor = null,
        IReportGenerator? reportGenerator = null,
        IReActPromptBuilder? promptBuilder = null,
        IReActLoopRunner? loopRunner = null,
        TokenUsageTracker? tokenUsageTracker = null,
        MultiModelLlmClient? multiModelLlmClient = null,
        IAgentCollaborationService? collaborationService = null,
        IReActTaskProcessor? taskProcessor = null)
        : this(
            personaLoader,
            messageBus,
            sharedMemory,
            toolRegistry,
            registry,
            ollamaClient,
            logger,
            loggerFactory,
            eventSink,
            actionParser,
            actionInputParser,
            actionExecutor,
            reportGenerator,
            promptBuilder,
            loopRunner,
            tokenUsageTracker,
            multiModelLlmClient,
            collaborationService,
            taskProcessor)
    {
        _vectorMemory = vectorMemory;
        _ragOptions = ragOptions.Value;
        _reActOptions = reActOptions.Value;
    }

    public AgentFactory(
        IPersonaLoader personaLoader,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        AgentRegistry registry,
        ILlmClient ollamaClient,
        ILogger<AgentFactory> logger,
        ILoggerFactory loggerFactory)
        : this(
            personaLoader,
            messageBus,
            sharedMemory,
            toolRegistry,
            registry,
            ollamaClient,
            logger,
            loggerFactory,
            eventSink: null)
    {
    }

    public AgentFactory(
        IPersonaLoader personaLoader,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        AgentRegistry registry,
        ILlmClient ollamaClient,
        ILogger<AgentFactory> logger,
        ILoggerFactory loggerFactory,
        IAgentEventSink? eventSink)
    {
        _personaLoader = personaLoader;
        _messageBus = messageBus;
        _sharedMemory = sharedMemory;
        _toolRegistry = toolRegistry;
        _registry = registry;
        _ollamaClient = ollamaClient;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _eventSink = eventSink;
        _vectorMemory = null;
        _ragOptions = new RagOptions();
        _reActOptions = new ReActOptions();
        _critiqueOptions = new CritiqueOptions();
        _tokenUsageTracker = null;
        _multiModelLlmClient = null;
        _collaborationService = null;
        _actionParser = null;
        _actionInputParser = null;
        _actionExecutor = null;
        _reportGenerator = null;
        _promptBuilder = null;
        _loopRunner = null;
        _taskProcessor = null;
    }

    public AgentFactory(
        IPersonaLoader personaLoader,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        AgentRegistry registry,
        ILlmClient ollamaClient,
        ILogger<AgentFactory> logger,
        ILoggerFactory loggerFactory,
        IAgentEventSink? eventSink,
        IActionParser? actionParser,
        IActionInputParser? actionInputParser,
        IActionExecutor? actionExecutor,
        IReportGenerator? reportGenerator,
        IReActPromptBuilder? promptBuilder,
        IReActLoopRunner? loopRunner,
        TokenUsageTracker? tokenUsageTracker,
        MultiModelLlmClient? multiModelLlmClient,
        IAgentCollaborationService? collaborationService,
        IReActTaskProcessor? taskProcessor)
        : this(
            personaLoader,
            messageBus,
            sharedMemory,
            toolRegistry,
            registry,
            ollamaClient,
            logger,
            loggerFactory,
            eventSink)
    {
        _actionParser = actionParser;
        _actionInputParser = actionInputParser;
        _actionExecutor = actionExecutor;
        _reportGenerator = reportGenerator;
        _promptBuilder = promptBuilder;
        _loopRunner = loopRunner;
        _tokenUsageTracker = tokenUsageTracker;
        _multiModelLlmClient = multiModelLlmClient;
        _collaborationService = collaborationService;
        _taskProcessor = taskProcessor;
    }

    public async Task<IAgent> CreateAgentAsync(string personaName, AgentRank rank, string? parentId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("🔨 Creating agent: {PersonaName} with rank {Rank}", personaName, rank);

        var personaKey = KeyNormalization.NormalizePersonaKey(personaName);

        // Load persona
        var persona = await _personaLoader.LoadPersonaAsync(personaName, ct);
        if (persona == null)
        {
            throw new InvalidOperationException($"Persona '{personaName}' not found");
        }

        persona.Name = personaName;

        return await CreateAgentAsync(persona, rank, parentId, personaPath: $"souls/{personaKey}.json", ct);

        // Create agent entity
        var agentEntity = new Agent
        {
            Id = Guid.NewGuid().ToString(),
            Name = personaName,
            Rank = rank,
            ParentAgentId = parentId,
            PersonaPath = $"souls/{personaKey}.json",
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
            _loggerFactory.CreateLogger<ReActAgent>(),
            _eventSink,
            _vectorMemory,
            _ragOptions,
            _reActOptions,
            _critiqueOptions,
            _tokenUsageTracker,
            _multiModelLlmClient,
            _collaborationService,
            _actionParser,
            _actionInputParser,
            _actionExecutor,
            _reportGenerator,
            _promptBuilder,
            _loopRunner,
            _taskProcessor);

        TryAppendAgentEvent(
            agentEntity.Id,
            EventType.AgentCreated,
            $"Agent created: {agentEntity.Name} ({agentEntity.Rank})",
            new Dictionary<string, object>
            {
                ["name"] = agentEntity.Name,
                ["rank"] = agentEntity.Rank.ToString(),
                ["parent_agent_id"] = agentEntity.ParentAgentId ?? string.Empty,
                ["persona_path"] = agentEntity.PersonaPath
            });

        // Register it
        RegisterAgent(agent);

        return agent;
    }

    public Task<IAgent> CreateAgentAsync(Persona persona, AgentRank rank, string? parentId = null, string? personaPath = null, CancellationToken ct = default)
    {
        if (persona == null) throw new ArgumentNullException(nameof(persona));

        var personaKey = KeyNormalization.NormalizePersonaKey(persona.Name);
        var resolvedPersonaPath = string.IsNullOrWhiteSpace(personaPath)
            ? $"souls/{personaKey}.json"
            : personaPath;

        _logger.LogInformation("🔨 Creating agent from dynamic persona: {PersonaName} with rank {Rank} (PersonaPath={PersonaPath})", persona.Name, rank, resolvedPersonaPath);

        var agentEntity = new Agent
        {
            Id = Guid.NewGuid().ToString(),
            Name = persona.Name,
            Rank = rank,
            ParentAgentId = parentId,
            PersonaPath = resolvedPersonaPath,
            Status = AgentStatus.Idle,
            CreatedAt = DateTime.UtcNow
        };

        var agent = new ReActAgent(
            agentEntity,
            persona,
            _messageBus,
            _sharedMemory,
            _toolRegistry,
            this,
            _ollamaClient,
            _loggerFactory.CreateLogger<ReActAgent>(),
            _eventSink,
            _vectorMemory,
            _ragOptions,
            _reActOptions,
            _critiqueOptions,
            _tokenUsageTracker,
            _multiModelLlmClient,
            _collaborationService,
            _actionParser,
            _actionInputParser,
            _actionExecutor,
            _reportGenerator,
            _promptBuilder,
            _loopRunner,
            _taskProcessor);

        TryAppendAgentEvent(
            agentEntity.Id,
            EventType.AgentCreated,
            $"Agent created: {agentEntity.Name} ({agentEntity.Rank})",
            new Dictionary<string, object>
            {
                ["name"] = agentEntity.Name,
                ["rank"] = agentEntity.Rank.ToString(),
                ["parent_agent_id"] = agentEntity.ParentAgentId ?? string.Empty,
                ["persona_path"] = agentEntity.PersonaPath
            });

        RegisterAgent(agent);
        return Task.FromResult<IAgent>(agent);
    }

    public IAgent? GetAgent(string agentId) => _registry.GetAgent(agentId);

    public IEnumerable<IAgent> GetAllAgents() => _registry.GetAllAgents();

    public void RegisterAgent(IAgent agent) => _registry.Register(agent);

    public void UnregisterAgent(string agentId) => _registry.Unregister(agentId);

    public async Task TerminateAgentAsync(string agentId, CancellationToken ct = default)
    {
        TryAppendAgentEvent(
            agentId,
            EventType.AgentTerminated,
            "Agent termination requested",
            new Dictionary<string, object>());

        await _registry.TerminateAgentAsync(agentId, ct);

        // Cleanup message bus
        if (_messageBus is ChannelMessageBus messageBus)
        {
            messageBus.CleanupAgent(agentId);
        }
    }

    private void TryAppendAgentEvent(string agentId, EventType type, string description, Dictionary<string, object> metadata)
    {
        if (_eventSink == null)
        {
            return;
        }

        try
        {
            _eventSink.AppendEvent(new AgentEvent
            {
                AgentId = agentId,
                Type = type,
                Description = description,
                Metadata = metadata
            });
        }
        catch
        {
            // best-effort
        }
    }
}
