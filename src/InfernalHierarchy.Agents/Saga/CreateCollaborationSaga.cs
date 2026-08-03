using InfernalHierarchy.Core.Saga;

namespace InfernalHierarchy.Agents.Saga;

/// <summary>
/// Saga for creating a multi-agent collaboration workflow
/// Demonstrates compensation: if any step fails, all previous steps are undone
/// </summary>
public class CreateCollaborationSaga : SagaBase
{
    /// <inheritdoc/>
    public override string Name => "CreateCollaboration";

    private readonly CollaborationRequest _collaborationRequest;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateCollaborationSaga"/> class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="agentFactory">Agent factory</param>
    /// <param name="memory">Shared memory</param>
    /// <param name="collaborationService">Collaboration service</param>
    /// <param name="collaborationRequest">Collaboration request</param>
    public CreateCollaborationSaga(
        ILogger<CreateCollaborationSaga> logger,
        IAgentFactory agentFactory,
        ISharedMemory memory,
        IAgentCollaborationService collaborationService,
        CollaborationRequest collaborationRequest)
        : base(logger)
    {
        _collaborationRequest = collaborationRequest;

        // Step 1: Validate participants
        AddStep(new ValidateParticipantsStep(logger, agentFactory));

        // Step 2: Store collaboration in memory
        AddStep(new StoreCollaborationStep(logger, memory));

        // Step 3: Send collaboration requests
        AddStep(new SendCollaborationRequestsStep(logger, collaborationService));

        // Step 4: Aggregate responses
        AddStep(new AggregateResponsesStep(logger, collaborationService));

        // Step 5: Store final result
        AddStep(new StoreFinalResultStep(logger, memory));
    }

    protected override SagaContext CreateContext()
    {
        var context = base.CreateContext();
        context.Data["CollaborationRequest"] = _collaborationRequest;
        return context;
    }
}

/// <summary>
/// Step 1: Validate all participants exist and are active
/// </summary>
public class ValidateParticipantsStep : ISagaStep
{
    private readonly ILogger _logger;
    private readonly IAgentFactory _agentFactory;

    /// <inheritdoc/>
    public string Name => "ValidateParticipants";

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidateParticipantsStep"/> class.
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="agentFactory">Agent factory</param>
    public ValidateParticipantsStep(ILogger logger, IAgentFactory agentFactory)
    {
        _logger = logger;
        _agentFactory = agentFactory;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogDebug("Validating participants for collaboration {SagaId}", context.SagaId);

        var request = (CollaborationRequest)context.Data["CollaborationRequest"];
        
        // Validate all participants exist (simplified - would check registry in real impl)
        if (request.ParticipantAgentIds.Count < request.MinimumParticipants)
        {
            throw new InvalidOperationException(
                $"Insufficient participants: {request.ParticipantAgentIds.Count} < {request.MinimumParticipants}");
        }

        context.Data["ValidatedParticipants"] = request.ParticipantAgentIds;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CompensateAsync(SagaContext context, CancellationToken ct = default)
    {
        // Nothing to compensate - read-only validation
        _logger.LogDebug("Compensation not needed for ValidateParticipants step");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Step 2: Store collaboration request in shared memory
/// </summary>
public class StoreCollaborationStep : ISagaStep
{
    private readonly ILogger _logger;
    private readonly ISharedMemory _memory;

    /// <inheritdoc/>
    public string Name => "StoreCollaboration";

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreCollaborationStep"/> class.
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="memory">Shared memory</param>
    public StoreCollaborationStep(ILogger logger, ISharedMemory memory)
    {
        _logger = logger;
        _memory = memory;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogDebug("Storing collaboration request for saga {SagaId}", context.SagaId);

        var request = (CollaborationRequest)context.Data["CollaborationRequest"];

        // Store as a decision in memory
        var decision = new Decision
        {
            Context = $"Collaboration: {request.Task}",
            Action = request.InitiatorAgentId,
            Reasoning = $"Strategy: {request.Strategy}, Participants: {request.ParticipantAgentIds.Count}",
            Outcome = $"Minimum confidence: {request.MinimumConfidence:P0}"
        };

        await _memory.AddDecisionAsync(decision, ct).ConfigureAwait(false);
        context.Data["DecisionId"] = decision.Id;
    }

    /// <inheritdoc/>
    public async Task CompensateAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogWarning("Compensating StoreCollaboration step - deleting decision");

        if (context.Data.TryGetValue("DecisionId", out var decisionId))
        {
            await _memory.DeleteDecisionAsync(decisionId.ToString()!, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Step 3: Send collaboration requests to all participants
/// </summary>
public class SendCollaborationRequestsStep : ISagaStep
{
    private readonly ILogger _logger;
    private readonly IAgentCollaborationService _collaborationService;

    /// <inheritdoc/>
    public string Name => "SendRequests";

    /// <summary>
    /// Initializes a new instance of the <see cref="SendCollaborationRequestsStep"/> class.
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="collaborationService">Collaboration service</param>
    public SendCollaborationRequestsStep(ILogger logger, IAgentCollaborationService collaborationService)
    {
        _logger = logger;
        _collaborationService = collaborationService;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogDebug("Sending collaboration requests for saga {SagaId}", context.SagaId);

        var request = (CollaborationRequest)context.Data["CollaborationRequest"];
        var result = await _collaborationService.RequestCollaborationAsync(request, ct).ConfigureAwait(false);

        context.Data["CollaborationRequestResult"] = result;
        context.Data["RequestsSent"] = true;
    }

    /// <inheritdoc/>
    public async Task CompensateAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogWarning("Compensating SendRequests step - sending cancellation messages");

        if (context.Data.TryGetValue("CollaborationRequest", out var requestObj)
            && requestObj is CollaborationRequest request
            && !string.IsNullOrWhiteSpace(request.Id))
        {
            await _collaborationService.CancelCollaborationAsync(request.Id, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Step 4: Aggregate responses from participants
/// </summary>
public class AggregateResponsesStep : ISagaStep
{
    private readonly ILogger _logger;
    private readonly IAgentCollaborationService _collaborationService;

    /// <inheritdoc/>
    public string Name => "AggregateResponses";

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateResponsesStep"/> class.
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="collaborationService">Collaboration service</param>
    public AggregateResponsesStep(ILogger logger, IAgentCollaborationService collaborationService)
    {
        _logger = logger;
        _collaborationService = collaborationService;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogDebug("Aggregating responses for saga {SagaId}", context.SagaId);

        if (!context.Data.TryGetValue("CollaborationRequestResult", out var resultObj)
            || resultObj is not CollaborationResult result)
        {
            throw new InvalidOperationException("Collaboration request result missing before aggregation step");
        }

        context.Data["AggregatedResult"] = result;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CompensateAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogWarning("Compensating AggregateResponses step");
        
        // Clear aggregated result
        context.Data.Remove("AggregatedResult");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Step 5: Store final collaboration result in memory
/// </summary>
public class StoreFinalResultStep : ISagaStep
{
    private readonly ILogger _logger;
    private readonly ISharedMemory _memory;

    /// <inheritdoc/>
    public string Name => "StoreFinalResult";

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreFinalResultStep"/> class.
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="memory">Shared memory</param>
    public StoreFinalResultStep(ILogger logger, ISharedMemory memory)
    {
        _logger = logger;
        _memory = memory;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogDebug("Storing final result for saga {SagaId}", context.SagaId);

        var result = (CollaborationResult)context.Data["AggregatedResult"];

        // Store as a fact
        var fact = new Fact
        {
            Category = "Collaboration",
            Content = $"Collaboration result: {result.Decision}",
            Source = "CollaborationSaga",
            Confidence = result.Confidence
        };

        await _memory.AddFactAsync(fact, ct).ConfigureAwait(false);
        context.Data["ResultFactId"] = fact.Id;
    }

    /// <inheritdoc/>
    public async Task CompensateAsync(SagaContext context, CancellationToken ct = default)
    {
        _logger.LogWarning("Compensating StoreFinalResult step - deleting fact");

        if (context.Data.TryGetValue("ResultFactId", out var factId))
        {
            await _memory.DeleteFactAsync(factId.ToString()!, ct).ConfigureAwait(false);
        }
    }
}
