using System.Collections.Concurrent;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Agents;

/// <summary>
/// Service for coordinating multi-agent collaboration and consensus decision-making.
/// Implements 5 collaboration strategies: Voting, WeightedVoting, Consensus, HighestConfidence, Hierarchical.
/// </summary>
public class AgentCollaborationService : IAgentCollaborationService
{
    private readonly ILogger<AgentCollaborationService> _logger;
    private readonly IMessageBus _messageBus;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ConcurrentDictionary<string, CollaborationRequest> _activeCollaborations;
    private readonly ConcurrentDictionary<string, List<AgentResponse>> _responses;

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
    {
        _logger = logger;
        _messageBus = messageBus;
        _agentRegistry = agentRegistry;
        _activeCollaborations = new ConcurrentDictionary<string, CollaborationRequest>();
        _responses = new ConcurrentDictionary<string, List<AgentResponse>>();
    }

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
        request.Status = CollaborationStatus.InProgress;

        // Send collaboration requests to all participants
        var tasks = new List<Task>();
        foreach (var participantId in request.ParticipantAgentIds)
        {
            var message = new AgentMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = MessageType.CollaborationRequest,
                Content = $"[COLLABORATION_REQUEST:{request.Id}] {request.Task}",
                FromAgentId = request.InitiatorAgentId,
                ToAgentId = participantId,
                Timestamp = DateTime.UtcNow,
                Payload = new Dictionary<string, object>
                {
                    ["CollaborationId"] = request.Id,
                    ["Strategy"] = request.Strategy.ToString(),
                    ["Timeout"] = request.Timeout.TotalSeconds
                }
            };

            tasks.Add(_messageBus.PublishAsync(message, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Wait for responses or timeout
        var deadline = DateTime.UtcNow.Add(request.Timeout);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (_responses.TryGetValue(request.Id, out var responses) &&
                responses.Count >= request.MinimumParticipants)
            {
                // Enough responses received, aggregate
                var result = await AggregateResponsesAsync(request, responses, ct).ConfigureAwait(false);
                request.Status = result.Confidence >= request.MinimumConfidence
                    ? CollaborationStatus.Completed
                    : CollaborationStatus.Failed;
                request.CompletedAt = DateTime.UtcNow;
                request.Result = result;

                _logger.LogInformation(
                    "Collaboration {RequestId} completed with {ParticipantCount} participants, confidence {Confidence:F2}",
                    request.Id, result.ParticipantCount, result.Confidence);

                return result;
            }

            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        // Timeout or cancellation
        request.Status = ct.IsCancellationRequested ? CollaborationStatus.Cancelled : CollaborationStatus.TimedOut;
        request.CompletedAt = DateTime.UtcNow;

        _logger.LogWarning(
            "Collaboration {RequestId} {Status} with only {ResponseCount}/{MinParticipants} responses",
            request.Id, request.Status, _responses.TryGetValue(request.Id, out var timeoutResponses) ? timeoutResponses.Count : 0,
            request.MinimumParticipants);

        // Return partial result if any responses
        if (_responses.TryGetValue(request.Id, out var partialResponses) && partialResponses.Count > 0)
        {
            return await AggregateResponsesAsync(request, partialResponses, ct).ConfigureAwait(false);
        }

        return new CollaborationResult
        {
            Decision = "TIMEOUT",
            Confidence = 0.0,
            AggregatedReasoning = "Collaboration timed out before receiving sufficient responses",
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

        var responseList = _responses.GetOrAdd(requestId, _ => new List<AgentResponse>());
        lock (responseList)
        {
            responseList.Add(response);
        }

        _logger.LogDebug(
            "Received collaboration response from agent {AgentId} for request {RequestId} (confidence: {Confidence:F2})",
            response.AgentId, requestId, response.Confidence);

        return Task.CompletedTask;
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

        return request.Strategy switch
        {
            CollaborationStrategy.Voting => AggregateByVoting(responses, request.Strategy),
            CollaborationStrategy.WeightedVoting => AggregateByWeightedVoting(responses, request.Strategy),
            CollaborationStrategy.Consensus => AggregateByConsensus(responses, request.Strategy),
            CollaborationStrategy.HighestConfidence => AggregateByHighestConfidence(responses, request.Strategy),
            CollaborationStrategy.Hierarchical => AggregateByHierarchical(responses, request.Strategy),
            _ => AggregateByWeightedVoting(responses, request.Strategy) // Default fallback
        };
    }

    /// <summary>
    /// Simple majority voting - most common response wins.
    /// </summary>
    private CollaborationResult AggregateByVoting(List<AgentResponse> responses, CollaborationStrategy strategy)
    {
        var grouped = responses
            .GroupBy(r => r.Response.Trim().ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .ToList();

        var winningGroup = grouped.FirstOrDefault();
        if (winningGroup == null)
        {
            return new CollaborationResult
            {
                Decision = "NO_CONSENSUS",
                Confidence = 0.0,
                Responses = responses,
                ParticipantCount = responses.Count,
                AgreementScore = 0.0,
                Strategy = strategy
            };
        }

        var winningResponse = winningGroup.OrderByDescending(r => r.Confidence).First();
        var agreementScore = (double)winningGroup.Count() / responses.Count;

        return new CollaborationResult
        {
            Decision = winningResponse.Response,
            Confidence = agreementScore, // Agreement percentage as confidence
            Responses = responses,
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            Strategy = strategy,
            WinningResponse = winningResponse,
            AggregatedReasoning = string.Join("\n\n", winningGroup.Select(r => $"[{r.AgentId}]: {r.Reasoning}"))
        };
    }

    /// <summary>
    /// Weighted voting based on agent rank and expertise.
    /// </summary>
    private CollaborationResult AggregateByWeightedVoting(List<AgentResponse> responses, CollaborationStrategy strategy)
    {
        var grouped = responses
            .GroupBy(r => r.Response.Trim().ToLowerInvariant())
            .Select(g => new
            {
                Response = g.First().Response,
                TotalWeight = g.Sum(r => r.Weight * r.Confidence),
                Responses = g.ToList(),
                AverageConfidence = g.Average(r => r.Confidence)
            })
            .OrderByDescending(g => g.TotalWeight)
            .ToList();

        var winner = grouped.FirstOrDefault();
        if (winner == null)
        {
            return new CollaborationResult
            {
                Decision = "NO_CONSENSUS",
                Confidence = 0.0,
                Responses = responses,
                ParticipantCount = responses.Count,
                AgreementScore = 0.0,
                Strategy = strategy
            };
        }

        var totalWeight = responses.Sum(r => r.Weight * r.Confidence);
        var agreementScore = winner.TotalWeight / totalWeight;

        return new CollaborationResult
        {
            Decision = winner.Response,
            Confidence = Math.Min(winner.AverageConfidence * agreementScore, 1.0),
            Responses = responses,
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            Strategy = strategy,
            WinningResponse = winner.Responses.OrderByDescending(r => r.Confidence).First(),
            AggregatedReasoning = string.Join("\n\n", winner.Responses.Select(r => $"[{r.AgentId} (weight: {r.Weight:F2})]: {r.Reasoning}"))
        };
    }

    /// <summary>
    /// All agents must agree (unanimous consensus).
    /// </summary>
    private CollaborationResult AggregateByConsensus(List<AgentResponse> responses, CollaborationStrategy strategy)
    {
        var uniqueResponses = responses
            .Select(r => r.Response.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (uniqueResponses.Count == 1)
        {
            // Perfect consensus
            var averageConfidence = responses.Average(r => r.Confidence);
            var firstResponse = responses.First();

            return new CollaborationResult
            {
                Decision = firstResponse.Response,
                Confidence = averageConfidence,
                Responses = responses,
                ParticipantCount = responses.Count,
                AgreementScore = 1.0,
                Strategy = strategy,
                WinningResponse = responses.OrderByDescending(r => r.Confidence).First(),
                AggregatedReasoning = string.Join("\n\n", responses.Select(r => $"[{r.AgentId}]: {r.Reasoning}"))
            };
        }

        // No consensus - return conflict summary
        var grouped = responses
            .GroupBy(r => r.Response.Trim().ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .ToList();

        var conflictSummary = string.Join("; ", grouped.Select(g => $"{g.Count()} voted '{g.First().Response}'"));

        return new CollaborationResult
        {
            Decision = $"NO_CONSENSUS: {conflictSummary}",
            Confidence = 0.0,
            Responses = responses,
            ParticipantCount = responses.Count,
            AgreementScore = (double)grouped.First().Count() / responses.Count,
            Strategy = strategy,
            AggregatedReasoning = "Agents could not reach unanimous agreement. " + conflictSummary
        };
    }

    /// <summary>
    /// Use the response with the highest confidence score.
    /// </summary>
    private CollaborationResult AggregateByHighestConfidence(List<AgentResponse> responses, CollaborationStrategy strategy)
    {
        var winner = responses.OrderByDescending(r => r.Confidence).First();

        // Calculate agreement score - how many agents gave similar response
        var similarResponses = responses
            .Where(r => r.Response.Trim().ToLowerInvariant() == winner.Response.Trim().ToLowerInvariant())
            .ToList();

        var agreementScore = (double)similarResponses.Count / responses.Count;

        return new CollaborationResult
        {
            Decision = winner.Response,
            Confidence = winner.Confidence,
            Responses = responses,
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            Strategy = strategy,
            WinningResponse = winner,
            AggregatedReasoning = $"Highest confidence response from {winner.AgentId}:\n{winner.Reasoning}"
        };
    }

    /// <summary>
    /// Hierarchical decision - higher ranks override lower ranks.
    /// </summary>
    private CollaborationResult AggregateByHierarchical(List<AgentResponse> responses, CollaborationStrategy strategy)
    {
        // Get response from highest-ranked agent
        var winner = responses
            .OrderByDescending(r => (int)r.AgentRank)
            .ThenByDescending(r => r.Confidence)
            .First();

        // Count how many agents at the same rank agreed
        var sameRankResponses = responses
            .Where(r => r.AgentRank == winner.AgentRank)
            .ToList();

        var sameRankAgreement = sameRankResponses
            .Count(r => r.Response.Trim().ToLowerInvariant() == winner.Response.Trim().ToLowerInvariant());

        var agreementScore = (double)sameRankAgreement / sameRankResponses.Count;

        return new CollaborationResult
        {
            Decision = winner.Response,
            Confidence = winner.Confidence * agreementScore,
            Responses = responses,
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            Strategy = strategy,
            WinningResponse = winner,
            AggregatedReasoning = $"Hierarchical decision from {winner.AgentRank} agent {winner.AgentId} " +
                                 $"(agreement among {winner.AgentRank} agents: {agreementScore:P0}):\n{winner.Reasoning}"
        };
    }
}
