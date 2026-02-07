using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using InfernalHierarchy.Agents.Collaboration.Strategies;

namespace InfernalHierarchy.Agents.Collaboration;

/// <summary>
/// Service for coordinating multi-agent collaboration and consensus decision-making.
/// Implements 5 collaboration strategies: Voting, WeightedVoting, Consensus, HighestConfidence, Hierarchical.
/// Enhanced with conflict resolution, multi-round consensus, and dynamic strategy selection.
/// </summary>
public class AgentCollaborationService : IAgentCollaborationService
{
    private readonly ILogger<AgentCollaborationService> _logger;
    private readonly IMessageBus _messageBus;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ConcurrentDictionary<string, CollaborationRequest> _activeCollaborations;
    private readonly ConcurrentDictionary<string, List<AgentResponse>> _responses;
    private readonly ConcurrentDictionary<string, int> _collaborationRounds;
    private readonly ConcurrentDictionary<string, DateTime> _roundStartTimes;
    private readonly ConcurrentDictionary<string, Channel<AgentResponse>> _responseChannels;
    private readonly IReadOnlyDictionary<CollaborationStrategy, IAggregationStrategy> _aggregationStrategies;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCollaborationService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="messageBus">Message bus for agent communication.</param>
    /// <param name="agentRegistry">Registry of active agents.</param>
    public AgentCollaborationService(
        ILogger<AgentCollaborationService> logger,
        IMessageBus messageBus,
        IAgentRegistry agentRegistry)
        : this(logger, messageBus, agentRegistry, CreateDefaultStrategies())
    {
    }

    public AgentCollaborationService(
        ILogger<AgentCollaborationService> logger,
        IMessageBus messageBus,
        IAgentRegistry agentRegistry,
        IEnumerable<IAggregationStrategy> aggregationStrategies)
    {
        _logger = logger;
        _messageBus = messageBus;
        _agentRegistry = agentRegistry;
        _activeCollaborations = new ConcurrentDictionary<string, CollaborationRequest>();
        _responses = new ConcurrentDictionary<string, List<AgentResponse>>();
        _collaborationRounds = new ConcurrentDictionary<string, int>();
        _roundStartTimes = new ConcurrentDictionary<string, DateTime>();
        _responseChannels = new ConcurrentDictionary<string, Channel<AgentResponse>>();

        _aggregationStrategies = (aggregationStrategies ?? CreateDefaultStrategies())
            .GroupBy(s => s.Strategy)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private Channel<AgentResponse> GetOrCreateResponseChannel(string requestId)
        => _responseChannels.GetOrAdd(
            requestId,
            _ => Channel.CreateUnbounded<AgentResponse>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true
            }));

    private void CleanupResponseChannel(string requestId)
    {
        if (_responseChannels.TryRemove(requestId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    private async Task WaitForResponseAsync(string requestId, TimeSpan timeout, CancellationToken ct)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        var channel = GetOrCreateResponseChannel(requestId);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            if (await channel.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                // Drain signals; responses are already stored in _responses.
                while (channel.Reader.TryRead(out _))
                {
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // timeout
        }
    }

    private static IEnumerable<IAggregationStrategy> CreateDefaultStrategies()
        =>
        [
            new VotingAggregationStrategy(),
            new WeightedVotingAggregationStrategy(),
            new ConsensusAggregationStrategy(),
            new HighestConfidenceAggregationStrategy(),
            new HierarchicalAggregationStrategy()
        ];

    private IAggregationStrategy GetAggregationStrategy(CollaborationStrategy strategy)
        => _aggregationStrategies.TryGetValue(strategy, out var found)
            ? found
            : _aggregationStrategies[CollaborationStrategy.WeightedVoting];

    /// <inheritdoc/>
    public async Task<CollaborationResult> RequestCollaborationAsync(
        CollaborationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Initiating collaboration request {RequestId} from agent {InitiatorId} with strategy {Strategy}",
            request.Id, request.InitiatorAgentId, request.Strategy);

        // Validate request
        if (request.ParticipantAgentIds.Count < request.MinimumParticipants)
        {
            _logger.LogWarning(
                "Collaboration {RequestId} has insufficient participants ({Count} < {Minimum})",
                request.Id, request.ParticipantAgentIds.Count, request.MinimumParticipants);
            
            return new CollaborationResult
            {
                Decision = "INSUFFICIENT_PARTICIPANTS",
                Confidence = 0.0,
                AggregatedReasoning = $"Need at least {request.MinimumParticipants} agents, only {request.ParticipantAgentIds.Count} available",
                Strategy = request.Strategy
            };
        }

        // Register active collaboration
        _activeCollaborations[request.Id] = request;
        _responses[request.Id] = new List<AgentResponse>();
        _ = GetOrCreateResponseChannel(request.Id);
        request.Status = CollaborationStatus.InProgress;

        // If the caller left the default strategy, try selecting a better one based on participants/task.
        if (request.Strategy == CollaborationStrategy.Voting)
        {
            request.Strategy = SelectOptimalStrategy(request);
        }

        const int maxRounds = 3;
        var currentTask = request.Task;

        for (var round = 1; round <= maxRounds; round++)
        {
            var proceedToNextRound = false;
            _collaborationRounds[request.Id] = round;
            _roundStartTimes[request.Id] = DateTime.UtcNow;

            await PublishCollaborationRoundAsync(request, currentTask, round, ct).ConfigureAwait(false);

            var deadline = DateTime.UtcNow.Add(request.Timeout);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var validResponses = GetValidResponses(request.Id, round);
                if (validResponses.Count >= request.MinimumParticipants)
                {
                    var result = await AggregateResponsesAsync(request, validResponses, ct).ConfigureAwait(false);

                    if (result.Confidence >= request.MinimumConfidence)
                    {
                        request.Status = CollaborationStatus.Completed;
                        request.CompletedAt = DateTime.UtcNow;
                        request.Result = result;
                        AnalyzeCollaborationHistory(request, result);
                        CleanupResponseChannel(request.Id);
                        return result;
                    }

                    // Conflict / low-confidence: attempt resolution.
                    var resolved = await ResolveConflictAsync(validResponses, request.Strategy, request.Id, ct).ConfigureAwait(false);
                    if (resolved.Decision != "CONFLICT_UNRESOLVED_RETRY")
                    {
                        request.Status = resolved.Confidence >= request.MinimumConfidence
                            ? CollaborationStatus.Completed
                            : CollaborationStatus.Failed;
                        request.CompletedAt = DateTime.UtcNow;
                        request.Result = resolved;
                        AnalyzeCollaborationHistory(request, resolved);
                        CleanupResponseChannel(request.Id);
                        return resolved;
                    }

                    // Multi-round refinement: ask agents to reconcile disagreements.
                    if (round < maxRounds)
                    {
                        currentTask = BuildRefinementTask(request.Task, validResponses, result, round + 1);

                        // Reset response collection for the next round
                        if (_responses.TryGetValue(request.Id, out var responseList))
                        {
                            lock (responseList)
                            {
                                responseList.Clear();
                            }
                        }

                        proceedToNextRound = true;
                        break; // exit collection loop; next round publishes immediately
                    }

                    request.Status = CollaborationStatus.Failed;
                    request.CompletedAt = DateTime.UtcNow;
                    request.Result = result;
                    AnalyzeCollaborationHistory(request, result);
                    CleanupResponseChannel(request.Id);
                    return result;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                // Wait for a response signal (or until this round times out).
                await WaitForResponseAsync(request.Id, remaining, ct).ConfigureAwait(false);
            }

            if (proceedToNextRound)
            {
                continue;
            }

            // Timeout or cancellation for this round.
            if (ct.IsCancellationRequested)
            {
                request.Status = CollaborationStatus.Cancelled;
                request.CompletedAt = DateTime.UtcNow;
                CleanupResponseChannel(request.Id);
                break;
            }

            var partial = GetValidResponses(request.Id, round);
            request.Status = CollaborationStatus.TimedOut;
            request.CompletedAt = DateTime.UtcNow;

            if (partial.Count > 0)
            {
                var partialResult = await AggregateResponsesAsync(request, partial, ct).ConfigureAwait(false);
                request.Result = partialResult;
                AnalyzeCollaborationHistory(request, partialResult);
                CleanupResponseChannel(request.Id);
                return partialResult;
            }

            CleanupResponseChannel(request.Id);
            return new CollaborationResult
            {
                Decision = "TIMEOUT",
                Confidence = 0.0,
                AggregatedReasoning = "Collaboration timed out before receiving sufficient responses",
                Strategy = request.Strategy
            };
        }

        CleanupResponseChannel(request.Id);
        return new CollaborationResult
        {
            Decision = request.Status == CollaborationStatus.Cancelled ? "CANCELLED" : "FAILED",
            Confidence = 0.0,
            AggregatedReasoning = "Collaboration did not complete successfully",
            Strategy = request.Strategy
        };
    }

    /// <inheritdoc/>
    public Task SubmitResponseAsync(
        string requestId,
        AgentResponse response,
        CancellationToken ct = default)
    {
        if (!_activeCollaborations.ContainsKey(requestId))
        {
            _logger.LogWarning(
                "Received response for unknown collaboration {RequestId} from agent {AgentId}",
                requestId, response.AgentId);
            return Task.CompletedTask;
        }

        if (response.Timestamp == default)
        {
            response.Timestamp = DateTime.UtcNow;
        }

        var currentRound = _collaborationRounds.TryGetValue(requestId, out var round)
            ? round
            : 1;

        if (response.Round <= 0)
        {
            response.Round = currentRound;
        }

        if (response.Round != currentRound)
        {
            _logger.LogDebug(
                "Ignoring response for collaboration {RequestId} from agent {AgentId} due to round mismatch (got {ResponseRound}, current {CurrentRound})",
                requestId,
                response.AgentId,
                response.Round,
                currentRound);
            return Task.CompletedTask;
        }

        if (_roundStartTimes.TryGetValue(requestId, out var roundStart) && response.Timestamp < roundStart)
        {
            _logger.LogDebug(
                "Ignoring stale response for collaboration {RequestId} from agent {AgentId}",
                requestId,
                response.AgentId);
            return Task.CompletedTask;
        }

        var responseList = _responses.GetOrAdd(requestId, _ => new List<AgentResponse>());
        lock (responseList)
        {
            responseList.Add(response);
        }

        _logger.LogDebug(
            "Received collaboration response from agent {AgentId} for request {RequestId} (confidence: {Confidence:F2})",
            response.AgentId, requestId, response.Confidence);

        // Signal any waiter that new responses arrived.
        _ = GetOrCreateResponseChannel(requestId).Writer.TryWrite(response);

        return Task.CompletedTask;
    }

    private async Task PublishCollaborationRoundAsync(
        CollaborationRequest request,
        string task,
        int round,
        CancellationToken ct)
    {
        var tasks = new List<Task>();
        foreach (var participantId in request.ParticipantAgentIds)
        {
            var message = new AgentMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = MessageType.CollaborationRequest,
                Content = $"[COLLABORATION_REQUEST:{request.Id}] {task}",
                FromAgentId = request.InitiatorAgentId,
                ToAgentId = participantId,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["CollaborationId"] = request.Id,
                    ["Strategy"] = request.Strategy.ToString(),
                    ["Timeout"] = request.Timeout.TotalSeconds,
                    ["Round"] = round
                }
            };

            tasks.Add(_messageBus.PublishAsync(message, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private List<AgentResponse> GetValidResponses(string requestId, int round)
    {
        if (!_responses.TryGetValue(requestId, out var responses))
        {
            return new List<AgentResponse>();
        }

        var roundStart = _roundStartTimes.TryGetValue(requestId, out var start)
            ? start
            : DateTime.MinValue;

        lock (responses)
        {
            return responses
                .Where(r => r.Round == round && r.Timestamp >= roundStart)
                .ToList();
        }
    }

    private static string BuildRefinementTask(
        string originalTask,
        List<AgentResponse> responses,
        CollaborationResult preliminary,
        int nextRound)
    {
        var grouped = responses
            .GroupBy(r => r.Response.Trim())
            .OrderByDescending(g => g.Count())
            .ToList();

        var conflictSummary = string.Join("\n", grouped.Select(g => $"- {g.Count()}×: {g.Key}"));
        var rationaleSummary = string.Join("\n\n", responses.Select(r => $"[{r.AgentId} | {r.AgentRank} | conf {r.Confidence:F2}]\n{r.Reasoning}"));

        return $"""
{originalTask}

---
Round {nextRound} refinement:
Agents disagreed on the decision. Review the competing options and reasoning below, then propose a revised decision that best satisfies the task.

Competing decisions:
{conflictSummary}

Reasoning:
{rationaleSummary}

Instruction: If you change your decision, explain why. If you keep it, explain why it dominates the alternatives.
""";
    }

    /// <inheritdoc/>
    public Task<CollaborationRequest?> GetCollaborationStatusAsync(
        string requestId,
        CancellationToken ct = default)
    {
        _activeCollaborations.TryGetValue(requestId, out var request);
        return Task.FromResult(request);
    }

    /// <inheritdoc/>
    public Task CancelCollaborationAsync(string requestId, CancellationToken ct = default)
    {
        if (_activeCollaborations.TryGetValue(requestId, out var request))
        {
            request.Status = CollaborationStatus.Cancelled;
            request.CompletedAt = DateTime.UtcNow;

            CleanupResponseChannel(requestId);

            _logger.LogInformation("Cancelled collaboration {RequestId}", requestId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<List<CollaborationRequest>> GetPendingCollaborationsAsync(
        string agentId,
        CancellationToken ct = default)
    {
        var pending = _activeCollaborations.Values
            .Where(c => c.ParticipantAgentIds.Contains(agentId) &&
                       (c.Status == CollaborationStatus.Pending || c.Status == CollaborationStatus.InProgress))
            .ToList();

        return Task.FromResult(pending);
    }

    /// <inheritdoc/>
    public Task<List<CollaborationRequest>> GetCollaborationHistoryAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        var history = _activeCollaborations.Values
            .Where(c => c.Status == CollaborationStatus.Completed ||
                       c.Status == CollaborationStatus.Failed ||
                       c.Status == CollaborationStatus.TimedOut ||
                       c.Status == CollaborationStatus.Cancelled)
            .OrderByDescending(c => c.CompletedAt ?? c.CreatedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult(history);
    }

    /// <inheritdoc/>
    public async Task<double> CalculateAgentWeightAsync(
        string agentId,
        AgentRank agentRank,
        string? toolName = null,
        CancellationToken ct = default)
    {
        // Base weight from rank
        var weight = agentRank switch
        {
            AgentRank.Supreme => 3.0,
            AgentRank.Prince => 2.0,
            AgentRank.Duke => 1.5,
            AgentRank.Worker => 1.0,
            _ => 1.0
        };

        await Task.CompletedTask;
        return weight;
    }

    /// <summary>
    /// Aggregates agent responses using the specified collaboration strategy.
    /// </summary>
    /// <param name="request">Collaboration request with strategy.</param>
    /// <param name="responses">List of agent responses.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated collaboration result.</returns>
    private async Task<CollaborationResult> AggregateResponsesAsync(
        CollaborationRequest request,
        List<AgentResponse> responses,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Aggregating {ResponseCount} responses for collaboration {RequestId} using {Strategy} strategy",
            responses.Count, request.Id, request.Strategy);

        // Calculate weights for weighted voting
        foreach (var response in responses)
        {
            response.Weight = await CalculateAgentWeightAsync(
                response.AgentId,
                response.AgentRank,
                null, // No specific tool context here
                ct).ConfigureAwait(false);
        }

        return GetAggregationStrategy(request.Strategy).Aggregate(responses);
    }

    /// <summary>
    /// Resolve conflicts when voting results in a tie or low confidence
    /// </summary>
    private async Task<CollaborationResult> ResolveConflictAsync(
        List<AgentResponse> responses,
        CollaborationStrategy originalStrategy,
        string requestId,
        CancellationToken ct)
    {
        _logger.LogWarning("Conflict detected in collaboration {RequestId}, initiating resolution", requestId);

        // Consensus should retry rather than silently switching strategy.
        if (originalStrategy == CollaborationStrategy.Consensus)
        {
            return BuildRetryResult(originalStrategy, requestId, responses);
        }

        // Voting/WeightedVoting: attempt stronger strategies in a predictable escalation order.
        if (originalStrategy == CollaborationStrategy.Voting)
        {
            _logger.LogInformation("Escalating to weighted voting for conflict resolution");
            return GetAggregationStrategy(CollaborationStrategy.WeightedVoting).Aggregate(responses);
        }

        if (originalStrategy == CollaborationStrategy.WeightedVoting || originalStrategy == CollaborationStrategy.Voting)
        {
            _logger.LogInformation("Escalating to hierarchical strategy for conflict resolution");
            return GetAggregationStrategy(CollaborationStrategy.Hierarchical).Aggregate(responses);
        }

        // Hierarchical can fall back to highest-confidence as a tie-breaker.
        if (originalStrategy == CollaborationStrategy.Hierarchical)
        {
            _logger.LogInformation("Using highest confidence for conflict resolution");
            return GetAggregationStrategy(CollaborationStrategy.HighestConfidence).Aggregate(responses);
        }

        // HighestConfidence (or other): multi-round refinement.
        return BuildRetryResult(originalStrategy, requestId, responses);
    }

    private CollaborationResult BuildRetryResult(
        CollaborationStrategy originalStrategy,
        string requestId,
        List<AgentResponse> responses)
    {
        var currentRound = _collaborationRounds.GetOrAdd(requestId, 0);
        if (currentRound < 3) // Max 3 rounds
        {
            _collaborationRounds[requestId] = currentRound + 1;
            _logger.LogInformation("Initiating round {Round} of multi-round refinement", currentRound + 1);

            return new CollaborationResult
            {
                Decision = "CONFLICT_UNRESOLVED_RETRY",
                Confidence = 0.0,
                Responses = responses,
                ParticipantCount = responses.Count,
                AgreementScore = 0.0,
                Strategy = originalStrategy,
                AggregatedReasoning = $"Round {currentRound + 1}/3: Agents disagreed, requesting refined responses with conflict context"
            };
        }

        _logger.LogError("Failed to resolve conflict in collaboration {RequestId} after 3 rounds", requestId);
        return new CollaborationResult
        {
            Decision = "CONFLICT_UNRESOLVED",
            Confidence = 0.0,
            Responses = responses,
            ParticipantCount = responses.Count,
            AgreementScore = 0.0,
            Strategy = originalStrategy,
            AggregatedReasoning = "Multiple strategies and rounds failed to resolve disagreement. Manual intervention may be required."
        };
    }

    /// <summary>
    /// Select optimal collaboration strategy based on task complexity and agent composition
    /// </summary>
    private CollaborationStrategy SelectOptimalStrategy(CollaborationRequest request)
    {
        var participantRanks = request.ParticipantAgentIds
            .Select(id => _agentRegistry.GetAgent(id))
            .Where(a => a != null)
            .Select(a => a!.Rank)
            .ToList();

        // If all same rank, use simple voting
        if (participantRanks.Distinct().Count() == 1)
        {
            _logger.LogInformation("All participants same rank, using Voting strategy");
            return CollaborationStrategy.Voting;
        }

        // If contains Supreme or Prince, use hierarchical
        if (participantRanks.Any(r => r == AgentRank.Supreme || r == AgentRank.Prince))
        {
            _logger.LogInformation("High-rank agents present, using Hierarchical strategy");
            return CollaborationStrategy.Hierarchical;
        }

        // If task requires high confidence (financial, security, critical decisions)
        var criticalKeywords = new[] { "financial", "security", "critical", "important", "urgent", "production" };
        if (criticalKeywords.Any(kw => request.Task.ToLowerInvariant().Contains(kw)))
        {
            _logger.LogInformation("Critical task detected, using Consensus strategy");
            return CollaborationStrategy.Consensus;
        }

        // Default to weighted voting for balanced approach
        _logger.LogInformation("Using WeightedVoting as default balanced strategy");
        return CollaborationStrategy.WeightedVoting;
    }

    /// <summary>
    /// Analyze collaboration history to improve future strategies
    /// </summary>
    private void AnalyzeCollaborationHistory(CollaborationRequest request, CollaborationResult result)
    {
        var avgConfidence = result.Responses.Average(r => r.Confidence);
        var avgLatency = result.Responses.Average(r => r.ProcessingTimeMs);
        var roundCount = _collaborationRounds.GetOrAdd(request.Id, 0);

        _logger.LogInformation(
            "Collaboration {RequestId} completed: Strategy={Strategy}, Agreement={Agreement:P0}, " +
            "Confidence={Confidence:F2}, Participants={Count}, Rounds={Rounds}, AvgLatency={Latency}ms",
            request.Id, request.Strategy, result.AgreementScore, result.Confidence,
            result.ParticipantCount, roundCount, avgLatency);

        // Store metrics for future strategy optimization
        // TODO: Integrate with AgentLearningService to track strategy effectiveness
    }
}
