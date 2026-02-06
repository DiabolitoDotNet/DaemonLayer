using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Clients;
using InfernalHierarchy.Tools.Telemetry;
using InfernalHierarchy.Agents.Base;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Agents.ReAct;

/// <summary>
/// ReAct (Reasoning + Acting) agent implementation
/// Implements the Thought → Action → Observation loop
/// </summary>
public class ReActAgent : BaseAgent
{
    private readonly IVectorMemory? _vectorMemory;
    private readonly RagOptions _ragOptions;
    private readonly IReActTaskProcessor _taskProcessor;
    private readonly ReActTaskProcessorContext _processorContext;
    private readonly IRagContextEnricher _ragContextEnricher;

    public ReActAgent(
        Agent agent,
        Persona persona,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        IAgentFactory agentFactory,
        ILlmClient ollamaClient,
        ILogger<ReActAgent> logger)
        : this(
            agent,
            persona,
            messageBus,
            sharedMemory,
            toolRegistry,
            agentFactory,
            ollamaClient,
            logger,
            eventSink: null)
    {
    }

    public ReActAgent(
        Agent agent,
        Persona persona,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        IAgentFactory agentFactory,
        ILlmClient ollamaClient,
        ILogger<ReActAgent> logger,
        IAgentEventSink? eventSink,
        IVectorMemory? vectorMemory = null,
        RagOptions? ragOptions = null,
        ReActOptions? reActOptions = null,
        TokenUsageTracker? tokenUsageTracker = null,
        MultiModelLlmClient? multiModelLlmClient = null,
        IAgentCollaborationService? collaborationService = null,
        IActionParser? actionParser = null,
        IActionInputParser? actionInputParser = null,
        IActionExecutor? actionExecutor = null,
        IReportGenerator? reportGenerator = null,
        IReActPromptBuilder? promptBuilder = null,
        IReActLoopRunner? loopRunner = null,
        IReActTaskProcessor? taskProcessor = null,
        IRagContextEnricher? ragContextEnricher = null,
        IAgentEventAppender? agentEventAppender = null)
        : base(agent, persona, messageBus, sharedMemory, toolRegistry, logger)
    {
        _vectorMemory = vectorMemory;
        _ragOptions = ragOptions ?? new RagOptions();
        var effectiveReActOptions = reActOptions ?? new ReActOptions();

        var effectiveActionParser = actionParser ?? new DefaultActionParser();
        var effectiveInputParser = actionInputParser ?? new DefaultActionInputParser(_logger);
        var effectiveActionExecutor = actionExecutor ?? new DefaultActionExecutor(effectiveInputParser);
        var effectiveReportGenerator = reportGenerator ?? new DefaultReportGenerator(tokenUsageTracker, multiModelLlmClient);

        var effectivePromptBuilder = promptBuilder ?? new DefaultReActPromptBuilder();
        var effectiveLoopRunner = loopRunner ?? new DefaultReActLoopRunner();

        _ragContextEnricher = ragContextEnricher ?? new DefaultRagContextEnricher();
        var effectiveEventAppender = agentEventAppender ?? new DefaultAgentEventAppender();

        _taskProcessor = taskProcessor ?? new DefaultReActTaskProcessor(_ragContextEnricher, effectiveEventAppender);

        _processorContext = new ReActTaskProcessorContext(
            AgentId: Id,
            AgentName: Name,
            AgentRank: Rank,
            Persona: Persona,
            LlmClient: ollamaClient,
            ToolRegistry: _toolRegistry,
            SharedMemory: _sharedMemory,
            ActionParser: effectiveActionParser,
            ActionExecutor: effectiveActionExecutor,
            ReportGenerator: effectiveReportGenerator,
            PromptBuilder: effectivePromptBuilder,
            LoopRunner: effectiveLoopRunner,
            ReActOptions: effectiveReActOptions,
            RagOptions: _ragOptions,
            VectorMemory: _vectorMemory,
            CollaborationService: collaborationService,
            EventSink: eventSink,
            SetStatus: s => Status = s,
            BuildBaseContextAsync: (message, token) => base.BuildContextAsync(message, token),
            Logger: _logger);

        _ = agentFactory;
    }

    protected override async Task<string> BuildContextAsync(AgentMessage task, CancellationToken ct)
    {
        var context = await base.BuildContextAsync(task, ct).ConfigureAwait(false);

        return await _ragContextEnricher.EnrichAsync(
            context,
            query: task.Content,
            agentId: Id,
            agentRank: Rank,
            vectorMemory: _vectorMemory,
            ragOptions: _ragOptions,
            logger: _logger,
            ct: ct).ConfigureAwait(false);
    }

    public override async Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
    {
        return await _taskProcessor.ProcessAsync(_processorContext, task, ct).ConfigureAwait(false);
    }
}
