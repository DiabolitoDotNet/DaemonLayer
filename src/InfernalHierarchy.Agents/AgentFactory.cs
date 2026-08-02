using InfernalHierarchy.Agents.Registry;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Tools;
using InfernalHierarchy.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IAgentQuotaService? _agentQuotaService;
    private readonly ISkillPackCatalog? _skillPackCatalog;
    private readonly IAgentSkillAssignmentPolicy? _skillAssignmentPolicy;
    private readonly IAgentSkillRuntimeStore? _skillRuntimeStore;
    private readonly IActionParser? _actionParser;
    private readonly IActionInputParser? _actionInputParser;
    private readonly IActionExecutor? _actionExecutor;
    private readonly IReportGenerator? _reportGenerator;
    private readonly IReActPromptBuilder? _promptBuilder;
    private readonly IReActLoopRunner? _loopRunner;
    private readonly IReActTaskProcessor? _taskProcessor;

    [ActivatorUtilitiesConstructor]
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
        IOptions<CritiqueOptions>? critiqueOptions = null,
        IAgentEventSink? eventSink = null,
        IActionParser? actionParser = null,
        IActionInputParser? actionInputParser = null,
        IActionExecutor? actionExecutor = null,
        IReportGenerator? reportGenerator = null,
        IReActPromptBuilder? promptBuilder = null,
        IReActLoopRunner? loopRunner = null,
        TokenUsageTracker? tokenUsageTracker = null,
        MultiModelLlmClient? multiModelLlmClient = null,
        IAgentCollaborationService? collaborationService = null,
        IAgentQuotaService? agentQuotaService = null,
        IReActTaskProcessor? taskProcessor = null,
        ISkillPackCatalog? skillPackCatalog = null,
        IAgentSkillAssignmentPolicy? skillAssignmentPolicy = null,
        IAgentSkillRuntimeStore? skillRuntimeStore = null)
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
            agentQuotaService,
            taskProcessor,
            skillPackCatalog,
            skillAssignmentPolicy,
            skillRuntimeStore)
    {
        _vectorMemory = vectorMemory;
        _ragOptions = ragOptions.Value;
        _reActOptions = reActOptions.Value;
        _critiqueOptions = critiqueOptions?.Value ?? new CritiqueOptions();
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
        IAgentEventSink? eventSink,
        IOptions<CritiqueOptions>? critiqueOptions = null,
        ISkillPackCatalog? skillPackCatalog = null,
        IAgentSkillAssignmentPolicy? skillAssignmentPolicy = null,
        IAgentSkillRuntimeStore? skillRuntimeStore = null)
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
        _critiqueOptions = critiqueOptions?.Value ?? new CritiqueOptions();
        _tokenUsageTracker = null;
        _multiModelLlmClient = null;
        _collaborationService = null;
        _agentQuotaService = null;
        _actionParser = null;
        _actionInputParser = null;
        _actionExecutor = null;
        _reportGenerator = null;
        _promptBuilder = null;
        _loopRunner = null;
        _taskProcessor = null;
        _skillPackCatalog = skillPackCatalog;
        _skillAssignmentPolicy = skillAssignmentPolicy;
        _skillRuntimeStore = skillRuntimeStore;
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
        IAgentQuotaService? agentQuotaService,
        IReActTaskProcessor? taskProcessor,
        ISkillPackCatalog? skillPackCatalog,
        IAgentSkillAssignmentPolicy? skillAssignmentPolicy,
        IAgentSkillRuntimeStore? skillRuntimeStore)
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
        _agentQuotaService = agentQuotaService;
        _taskProcessor = taskProcessor;
        _skillPackCatalog = skillPackCatalog;
        _skillAssignmentPolicy = skillAssignmentPolicy;
        _skillRuntimeStore = skillRuntimeStore;
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
    }

    public async Task<IAgent> CreateAgentAsync(Persona persona, AgentRank rank, string? parentId = null, string? personaPath = null, CancellationToken ct = default)
    {
        if (persona == null) throw new ArgumentNullException(nameof(persona));

        var personaKey = KeyNormalization.NormalizePersonaKey(persona.Name);
        var resolvedPersonaPath = string.IsNullOrWhiteSpace(personaPath)
            ? $"souls/{personaKey}.json"
            : personaPath;

        _logger.LogInformation("🔨 Creating agent from dynamic persona: {PersonaName} with rank {Rank} (PersonaPath={PersonaPath})", persona.Name, rank, resolvedPersonaPath);

        return await CreateAgentFromPersonaAsync(persona, personaKey, rank, parentId, resolvedPersonaPath, ct);
    }

    private async Task<IAgent> CreateAgentFromPersonaAsync(Persona persona, string personaKey, AgentRank rank, string? parentId, string? personaPathOverride = null, CancellationToken ct = default)
    {
        _agentQuotaService?.EnsureCanCreateAgent(rank);

        var effectivePersona = await ApplyBaseSkillPacksAsync(persona, rank, parentId, ct);

        // Telegram routes messages to the main agent using a stable id ("lucifer").
        // Keep this id stable to preserve routing and avoid orphaned channels.
        var agentId = (rank == AgentRank.Supreme && parentId == null &&
                       string.Equals(personaKey, "lucifer", StringComparison.OrdinalIgnoreCase))
            ? "lucifer"
            : Guid.NewGuid().ToString();

        var agentEntity = new Agent
        {
            Id = agentId,
            Name = persona.Name,
            Rank = rank,
            ParentAgentId = parentId,
            PersonaPath = personaPathOverride ?? $"souls/{personaKey}.json",
            Status = AgentStatus.Idle,
            CreatedAt = DateTime.UtcNow
        };

        var agent = new ReActAgent(
            agentEntity,
            effectivePersona,
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
            _taskProcessor,
            runtimeSkillStore: _skillRuntimeStore);

        TryAppendAgentEvent(
            agentEntity.Id,
            EventType.AgentCreated,
            $"Agent created: {agentEntity.Name} ({agentEntity.Rank})",
            new Dictionary<string, object>
            {
                ["name"] = agentEntity.Name,
                ["rank"] = agentEntity.Rank.ToString(),
                ["parent_agent_id"] = agentEntity.ParentAgentId ?? string.Empty,
                ["persona_path"] = agentEntity.PersonaPath,
                ["assigned_skill_packs"] = effectivePersona.CustomInstructions.TryGetValue("assigned_skill_packs", out var assigned)
                    ? assigned
                    : string.Empty
            });

        RegisterAgent(agent);
        return agent;
    }

    private async Task<Persona> ApplyBaseSkillPacksAsync(Persona persona, AgentRank rank, string? parentId, CancellationToken ct)
    {
        if (_skillAssignmentPolicy == null || _skillPackCatalog == null)
        {
            return persona;
        }

        var selectedPackIds = await _skillAssignmentPolicy.SelectInitialSkillPackIdsAsync(persona, rank, parentId, ct);
        if (selectedPackIds.Count == 0)
        {
            return persona;
        }

        var tools = new HashSet<string>(persona.AvailableTools, StringComparer.OrdinalIgnoreCase);
        var specializations = new HashSet<string>(persona.Specializations, StringComparer.OrdinalIgnoreCase);
        var instructions = new Dictionary<string, string>(persona.CustomInstructions, StringComparer.OrdinalIgnoreCase);
        var promptFragments = new List<string>();
        var applied = new List<string>();

        foreach (var packId in selectedPackIds)
        {
            var pack = await _skillPackCatalog.GetByIdAsync(packId, ct);
            if (pack == null || !pack.Enabled)
            {
                continue;
            }

            if (pack.AllowedRanks.Count > 0 && !pack.AllowedRanks.Any(r => string.Equals(r, rank.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var tool in pack.AdditionalTools)
            {
                if (!string.IsNullOrWhiteSpace(tool))
                {
                    tools.Add(tool.Trim());
                }
            }

            foreach (var specialization in pack.AdditionalSpecializations)
            {
                if (!string.IsNullOrWhiteSpace(specialization))
                {
                    specializations.Add(specialization.Trim());
                }
            }

            foreach (var kv in pack.CustomInstructions)
            {
                instructions[$"skill.{pack.Id}.{kv.Key}"] = kv.Value;
            }

            foreach (var fragment in pack.PromptFragments)
            {
                if (!string.IsNullOrWhiteSpace(fragment))
                {
                    promptFragments.Add(fragment.Trim());
                }
            }

            applied.Add(pack.Id);
        }

        if (applied.Count == 0)
        {
            return persona;
        }

        instructions["assigned_skill_packs"] = string.Join(",", applied.Distinct(StringComparer.OrdinalIgnoreCase));

        var mergedPrompt = persona.SystemPrompt;
        if (promptFragments.Count > 0)
        {
            mergedPrompt = $"{persona.SystemPrompt}{Environment.NewLine}{Environment.NewLine}# Skill Pack Guidance{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", promptFragments)}";
        }

        return new Persona
        {
            Name = persona.Name,
            DemonTitle = persona.DemonTitle,
            SystemPrompt = mergedPrompt,
            ModelOverride = persona.ModelOverride,
            Personality = persona.Personality,
            AvailableTools = tools.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray(),
            Specializations = specializations.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray(),
            CustomInstructions = instructions
        };
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
