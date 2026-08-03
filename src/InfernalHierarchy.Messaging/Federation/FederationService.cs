using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace InfernalHierarchy.Messaging.Federation;

/// <summary>
/// Implements federation between multiple InfernalHierarchy instances using HTTP
/// </summary>
public class FederationService : IFederationService
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<FederationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, FederatedInstance> _instances = new();
    private readonly string _localInstanceId;
    private readonly IAgentCollaborationService? _localCollaborationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="httpClient">HTTP client for remote communication</param>
    /// <param name="localInstanceId">Local instance identifier</param>
    public FederationService(
        ILogger<FederationService> logger,
        HttpClient httpClient,
        string localInstanceId)
        : this(logger, httpClient, localInstanceId, localCollaborationService: null)
    {
    }

    public FederationService(
        ILogger<FederationService> logger,
        HttpClient httpClient,
        string localInstanceId,
        IAgentCollaborationService? localCollaborationService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _localInstanceId = localInstanceId;
        _localCollaborationService = localCollaborationService;
    }

    /// <inheritdoc/>
    public Task RegisterInstanceAsync(FederatedInstance instance, CancellationToken ct = default)
    {
        _logger.LogInformation("Registering federated instance {InstanceId} ({Name}) at {BaseUrl}",
            instance.InstanceId, instance.Name, instance.BaseUrl);

        instance.LastHeartbeat = DateTime.UtcNow;
        _instances[instance.InstanceId] = instance;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UnregisterInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Unregistering federated instance {InstanceId}", instanceId);
        _instances.TryRemove(instanceId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<List<FederatedInstance>> GetActiveInstancesAsync(CancellationToken ct = default)
    {
        var activeInstances = _instances.Values
            .Where(i => i.IsActive && (DateTime.UtcNow - i.LastHeartbeat).TotalSeconds < 60)
            .ToList();

        return Task.FromResult(activeInstances);
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(FederatedMessage message, CancellationToken ct = default)
    {
        await SendMessageWithOptionalResponseAsync(message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task BroadcastMessageAsync(FederatedMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation("Broadcasting {MessageType} to all instances", message.MessageType);

        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);
        var tasks = instances
            .Where(i => i.InstanceId != _localInstanceId)
            .Select(i =>
            {
                var msg = new FederatedMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    SourceInstanceId = message.SourceInstanceId,
                    TargetInstanceId = i.InstanceId,
                    MessageType = message.MessageType,
                    Payload = message.Payload,
                    TtlSeconds = message.TtlSeconds,
                    RequiresResponse = false
                };
                return SendMessageAsync(msg, ct);
            });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> DelegateTaskAsync(TaskEntry task, CancellationToken ct = default)
    {
        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);

        var candidateInstances = instances
            .Where(i => i.InstanceId != _localInstanceId)
            .Where(i => i.CurrentAgentCount < i.MaxAgents)
            .OrderBy(i => i.CurrentLoad)
            .ThenBy(i => i.CurrentAgentCount)
            .ToList();

        if (candidateInstances.Count == 0)
        {
            _logger.LogWarning("No available instances for task delegation");
            return null;
        }

        foreach (var targetInstance in candidateInstances)
        {
            _logger.LogInformation("Delegating task {TaskId} to instance {InstanceId} (load: {Load:P0})",
                task.Id, targetInstance.InstanceId, targetInstance.CurrentLoad);

            var message = new FederatedMessage
            {
                SourceInstanceId = _localInstanceId,
                TargetInstanceId = targetInstance.InstanceId,
                MessageType = FederatedMessageType.DelegateTask,
                Payload = new Dictionary<string, object>
                {
                    ["TaskId"] = task.Id,
                    ["Task"] = JsonSerializer.Serialize(task)
                },
                RequiresResponse = true
            };

            var response = await SendMessageWithOptionalResponseAsync(message, ct).ConfigureAwait(false);
            if (response != null)
            {
                return targetInstance.InstanceId;
            }

            _logger.LogWarning(
                "Delegation attempt failed for task {TaskId} on instance {InstanceId}; trying next candidate",
                task.Id,
                targetInstance.InstanceId);
        }

        _logger.LogWarning("All delegation candidates failed for task {TaskId}", task.Id);
        return null;
    }

    /// <inheritdoc/>
    public async Task<CollaborationResult> RequestCrossInstanceCollaborationAsync(
        CollaborationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting cross-instance collaboration for task: {Task}", request.Task);

        var remoteInstances = GetActiveRemoteInstancesSnapshot();

        var responses = new List<AgentResponse>(remoteInstances.Count);
        var responseLock = new object();
        var tasks = new List<Task>(remoteInstances.Count);

        foreach (var instance in remoteInstances)
        {
            tasks.Add(CollectRemoteResponseAsync(instance));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (responses.Count == 0)
        {
            return await ExecuteLocalCollaborationFallbackAsync(
                request,
                remoteResponses: responses,
                reasonCode: "cross_instance_no_responses",
                reasonDescription: "No remote collaboration response received from active instances.",
                ct).ConfigureAwait(false);
        }

        var collectedResponses = responses;
        if (collectedResponses.Count < request.MinimumParticipants)
        {
            return await ExecuteLocalCollaborationFallbackAsync(
                request,
                remoteResponses: collectedResponses,
                reasonCode: "cross_instance_minimum_participants_not_met",
                reasonDescription: $"Received {collectedResponses.Count} responses but minimum participants is {request.MinimumParticipants}.",
                ct).ConfigureAwait(false);
        }

        var strategyOutcome = AggregateByStrategy(request.Strategy, collectedResponses, request.MinimumConfidence);
        if (!strategyOutcome.IsResolved)
        {
            return ExecuteSupervisorAdjudicationWorkflow(request, collectedResponses, strategyOutcome);
        }

        var sourceInstances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var response in collectedResponses)
        {
            var source = TryReadSourceInstanceFromReasoning(response.Reasoning);
            if (!string.IsNullOrWhiteSpace(source))
            {
                sourceInstances.Add(source);
            }
        }

        var sourceSummary = sourceInstances.Count == 0
            ? "unknown"
            : string.Join(", ", sourceInstances.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

        return new CollaborationResult
        {
            Decision = strategyOutcome.Decision,
            Confidence = strategyOutcome.Confidence,
            Responses = collectedResponses,
            ParticipantCount = collectedResponses.Count,
            AgreementScore = strategyOutcome.AgreementScore,
            WinningResponse = strategyOutcome.WinningResponse,
            AggregatedReasoning = $"Aggregated {collectedResponses.Count} cross-instance responses from [{sourceSummary}] using {request.Strategy}: {strategyOutcome.Reasoning}",
            Strategy = request.Strategy
        };

        async Task CollectRemoteResponseAsync(FederatedInstance instance)
        {
            var message = new FederatedMessage
            {
                SourceInstanceId = _localInstanceId,
                TargetInstanceId = instance.InstanceId,
                MessageType = FederatedMessageType.CollaborationRequest,
                Payload = new Dictionary<string, object>
                {
                    ["RequestId"] = request.Id,
                    ["Task"] = request.Task,
                    ["Strategy"] = request.Strategy.ToString()
                },
                RequiresResponse = true,
                TtlSeconds = (int)request.Timeout.TotalSeconds
            };

            try
            {
                var responseMessage = await SendMessageWithOptionalResponseAsync(message, ct).ConfigureAwait(false);
                if (TryExtractAgentResponse(responseMessage, instance.InstanceId, out var response))
                {
                    lock (responseLock)
                    {
                        responses.Add(response);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get collaboration response from {InstanceId}",
                    instance.InstanceId);
            }
        }
    }

    private static StrategyAggregationOutcome AggregateByStrategy(
        CollaborationStrategy strategy,
        List<AgentResponse> responses,
        double minimumConfidence)
    {
        return strategy switch
        {
            CollaborationStrategy.Consensus => AggregateConsensus(responses, minimumConfidence),
            CollaborationStrategy.WeightedVoting => AggregateWeightedVoting(responses, minimumConfidence),
            CollaborationStrategy.HighestConfidence => AggregateHighestConfidence(responses, minimumConfidence),
            CollaborationStrategy.Hierarchical => AggregateHierarchical(responses, minimumConfidence),
            _ => AggregateVoting(responses, minimumConfidence),
        };
    }

    private static StrategyAggregationOutcome AggregateVoting(List<AgentResponse> responses, double minimumConfidence)
    {
        var groups = GroupByResponse(responses);
        if (groups.Count == 0)
        {
            return StrategyAggregationOutcome.Unresolved("cross_instance_empty_responses", "No responses to aggregate.");
        }

        ResponseGroup? winner = null;
        ResponseGroup? runnerUp = null;

        foreach (var group in groups)
        {
            if (winner is null || IsVotingBetter(group, winner))
            {
                runnerUp = winner;
                winner = group;
                continue;
            }

            if (runnerUp is null || IsVotingBetter(group, runnerUp))
            {
                runnerUp = group;
            }
        }

        if (winner is null)
        {
            return StrategyAggregationOutcome.Unresolved("cross_instance_empty_responses", "No responses to aggregate.");
        }

        if (runnerUp is not null
            && winner.Count == runnerUp.Count
            && Math.Abs(winner.AverageConfidence - runnerUp.AverageConfidence) < 0.0001)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_voting_tie",
                "Voting resulted in a tie across candidate decisions.");
        }

        var confidence = winner.AverageConfidence;
        var agreement = (double)winner.Count / responses.Count;

        if (confidence < minimumConfidence)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_confidence_below_threshold",
                $"Voting winner confidence {confidence:0.00} is below threshold {minimumConfidence:0.00}.",
                confidence,
                agreement);
        }

        return StrategyAggregationOutcome.Resolved(
            winner.Response,
            confidence,
            agreement,
            winner.BestResponse,
            "majority voting converged");
    }

    private static StrategyAggregationOutcome AggregateWeightedVoting(List<AgentResponse> responses, double minimumConfidence)
    {
        var groups = GroupByResponse(responses);
        if (groups.Count == 0)
        {
            return StrategyAggregationOutcome.Unresolved("cross_instance_empty_responses", "No responses to aggregate.");
        }

        ResponseGroup? winner = null;
        double winnerWeight = 0;
        double secondWeight = 0;
        double totalWeight = 0;

        foreach (var group in groups)
        {
            var groupWeight = group.WeightSum;

            totalWeight += groupWeight;

            if (winner is null
                || groupWeight > winnerWeight
                || (Math.Abs(groupWeight - winnerWeight) < 0.0001 && group.AverageConfidence > winner.AverageConfidence))
            {
                secondWeight = winnerWeight;
                winnerWeight = groupWeight;
                winner = group;
                continue;
            }

            if (groupWeight > secondWeight)
            {
                secondWeight = groupWeight;
            }
        }

        if (winner is null)
        {
            return StrategyAggregationOutcome.Unresolved("cross_instance_empty_responses", "No responses to aggregate.");
        }

        if (Math.Abs(winnerWeight - secondWeight) < 0.0001)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_weighted_tie",
                "Weighted voting resulted in equal scores.");
        }

        var confidence = winner.AverageConfidence;
        var agreement = totalWeight <= 0 ? 0 : winnerWeight / totalWeight;

        if (confidence < minimumConfidence)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_confidence_below_threshold",
                $"Weighted winner confidence {confidence:0.00} is below threshold {minimumConfidence:0.00}.",
                confidence,
                agreement);
        }

        return StrategyAggregationOutcome.Resolved(
            winner.Response,
            confidence,
            agreement,
            winner.BestResponse,
            "weighted voting converged");
    }

    private static StrategyAggregationOutcome AggregateConsensus(List<AgentResponse> responses, double minimumConfidence)
    {
        var groups = GroupByResponse(responses);

        if (groups.Count != 1)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_consensus_not_reached",
                "Consensus strategy requires all participants to return the same decision.");
        }

        var winner = groups[0];
        if (winner.AverageConfidence < minimumConfidence)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_confidence_below_threshold",
                $"Consensus confidence {winner.AverageConfidence:0.00} is below threshold {minimumConfidence:0.00}.",
                winner.AverageConfidence,
                1);
        }

        return StrategyAggregationOutcome.Resolved(
            winner.Response,
            winner.AverageConfidence,
            1,
            winner.BestResponse,
            "consensus reached");
    }

    private static StrategyAggregationOutcome AggregateHighestConfidence(List<AgentResponse> responses, double minimumConfidence)
    {
        AgentResponse? winner = null;
        foreach (var response in responses)
        {
            if (winner is null || response.Confidence > winner.Confidence)
            {
                winner = response;
            }
        }

        if (winner is null)
        {
            return StrategyAggregationOutcome.Unresolved("cross_instance_empty_responses", "No responses to aggregate.");
        }

        if (winner.Confidence < minimumConfidence)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_confidence_below_threshold",
                $"Highest confidence response {winner.Confidence:0.00} is below threshold {minimumConfidence:0.00}.",
                winner.Confidence,
                0);
        }

        return StrategyAggregationOutcome.Resolved(
            winner.Response,
            winner.Confidence,
            0,
            winner,
            "selected highest confidence response");
    }

    private static StrategyAggregationOutcome AggregateHierarchical(List<AgentResponse> responses, double minimumConfidence)
    {
        AgentResponse? winner = null;
        var winnerRank = int.MaxValue;

        foreach (var response in responses)
        {
            var rank = RankPriority(response.AgentRank);
            if (winner is null || rank < winnerRank || (rank == winnerRank && response.Confidence > winner.Confidence))
            {
                winner = response;
                winnerRank = rank;
            }
        }

        if (winner is null)
        {
            return StrategyAggregationOutcome.Unresolved("cross_instance_empty_responses", "No responses to aggregate.");
        }

        if (winner.Confidence < minimumConfidence)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_confidence_below_threshold",
                $"Hierarchical winner confidence {winner.Confidence:0.00} is below threshold {minimumConfidence:0.00}.",
                winner.Confidence,
                0);
        }

        return StrategyAggregationOutcome.Resolved(
            winner.Response,
            winner.Confidence,
            0,
            winner,
            "selected highest-rank response");
    }

    private static int RankPriority(AgentRank rank)
    {
        return rank switch
        {
            AgentRank.Supreme => 0,
            AgentRank.Prince => 1,
            AgentRank.Duke => 2,
            _ => 3,
        };
    }

    private static CollaborationResult ExecuteSupervisorAdjudicationWorkflow(
        CollaborationRequest request,
        List<AgentResponse> responses,
        StrategyAggregationOutcome unresolved)
    {
        AgentResponse? supervisorDecision = null;
        var supervisorPriority = int.MaxValue;

        foreach (var response in responses)
        {
            if (string.IsNullOrWhiteSpace(response.Response))
            {
                continue;
            }

            var priority = RankPriority(response.AgentRank);
            if (supervisorDecision is null
                || priority < supervisorPriority
                || (priority == supervisorPriority && response.Confidence > supervisorDecision.Confidence)
                || (priority == supervisorPriority
                    && Math.Abs(response.Confidence - supervisorDecision.Confidence) < 0.0001
                    && string.CompareOrdinal(response.AgentId, supervisorDecision.AgentId) < 0))
            {
                supervisorDecision = response;
                supervisorPriority = priority;
            }
        }

        if (supervisorDecision is null)
        {
            return new CollaborationResult
            {
                Decision = "AUTONOMOUS_ADJUDICATION_FAILED",
                Confidence = unresolved.Confidence,
                Responses = responses,
                ParticipantCount = responses.Count,
                AgreementScore = unresolved.AgreementScore,
                AggregatedReasoning =
                    $"Autonomous supervisor adjudication workflow executed but no usable response was available ({unresolved.ReasonCode}).",
                Strategy = request.Strategy,
                ConflictClass = "unresolved",
                ConflictReasonCode = "autonomous_supervisor_adjudication_failed",
                NextAction = "none",
                NeedsSupervisorIntervention = false
            };
        }

        var agreementCount = responses.Count(r =>
            !string.IsNullOrWhiteSpace(r.Response)
            && string.Equals(r.Response.Trim(), supervisorDecision.Response.Trim(), StringComparison.OrdinalIgnoreCase));

        var agreementScore = responses.Count == 0 ? 0 : agreementCount / (double)responses.Count;

        return new CollaborationResult
        {
            Decision = supervisorDecision.Response,
            Confidence = Math.Max(supervisorDecision.Confidence, unresolved.Confidence),
            Responses = responses,
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            WinningResponse = supervisorDecision,
            AggregatedReasoning =
                $"Autonomous supervisor adjudication workflow resolved conflict ({unresolved.ReasonCode}) by selecting '{supervisorDecision.Response}' from {supervisorDecision.AgentId} ({supervisorDecision.AgentRank}, confidence={supervisorDecision.Confidence:F2}).",
            Strategy = CollaborationStrategy.Hierarchical,
            ConflictClass = "resolved",
            ConflictReasonCode = "resolved_by_supervisor_adjudication_workflow",
            NextAction = "none",
            NeedsSupervisorIntervention = false
        };
    }

    private static List<ResponseGroup> GroupByResponse(List<AgentResponse> responses)
    {
        var groups = new Dictionary<string, ResponseAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var response in responses)
        {
            if (string.IsNullOrWhiteSpace(response.Response))
            {
                continue;
            }

            var key = response.Response.Trim();
            if (!groups.TryGetValue(key, out var acc))
            {
                acc = new ResponseAccumulator(key);
                groups[key] = acc;
            }

            acc.Add(response);
        }

        var result = new List<ResponseGroup>(groups.Count);
        foreach (var pair in groups)
        {
            result.Add(pair.Value.ToGroup());
        }

        return result;
    }

    private static bool IsVotingBetter(ResponseGroup candidate, ResponseGroup current)
    {
        return candidate.Count > current.Count
            || (candidate.Count == current.Count && candidate.AverageConfidence > current.AverageConfidence);
    }

    private async Task<FederatedMessage?> SendMessageWithOptionalResponseAsync(
        FederatedMessage message,
        CancellationToken ct)
    {
        if (!_instances.TryGetValue(message.TargetInstanceId, out var targetInstance))
        {
            _logger.LogWarning("Target instance {InstanceId} not found", message.TargetInstanceId);
            return null;
        }

        _logger.LogDebug("Sending {MessageType} to instance {InstanceId}",
            message.MessageType, message.TargetInstanceId);

        try
        {
            var endpoint = $"{targetInstance.BaseUrl}/api/federation/message";
            var response = await _httpClient.PostAsJsonAsync(endpoint, message, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (!message.RequiresResponse)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<FederatedMessage>(cancellationToken: ct)
                .ConfigureAwait(false);

            _logger.LogDebug("Received response from {InstanceId}: {CorrelationId}",
                message.TargetInstanceId, result?.CorrelationId);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send message to instance {InstanceId}", message.TargetInstanceId);
            targetInstance.IsActive = false;
            return null;
        }
    }

    private static bool TryExtractAgentResponse(
        FederatedMessage? responseMessage,
        string sourceInstanceId,
        out AgentResponse response)
    {
        response = new AgentResponse();
        if (responseMessage?.Payload == null || responseMessage.Payload.Count == 0)
        {
            return false;
        }

        var payload = responseMessage.Payload;
        object? rawAgentResponse = null;
        string? decision = null;
        string? responseField = null;
        string? rawAgentId = null;
        string? reasoning = null;
        object? rawConfidence = null;

        foreach (var kvp in payload)
        {
            if (string.Equals(kvp.Key, "AgentResponse", StringComparison.OrdinalIgnoreCase))
            {
                rawAgentResponse = kvp.Value;
                continue;
            }

            if (string.Equals(kvp.Key, "Decision", StringComparison.OrdinalIgnoreCase))
            {
                decision = ConvertPayloadToString(kvp.Value);
                continue;
            }

            if (string.Equals(kvp.Key, "Response", StringComparison.OrdinalIgnoreCase))
            {
                responseField = ConvertPayloadToString(kvp.Value);
                continue;
            }

            if (string.Equals(kvp.Key, "AgentId", StringComparison.OrdinalIgnoreCase))
            {
                rawAgentId = ConvertPayloadToString(kvp.Value);
                continue;
            }

            if (string.Equals(kvp.Key, "Reasoning", StringComparison.OrdinalIgnoreCase))
            {
                reasoning = ConvertPayloadToString(kvp.Value);
                continue;
            }

            if (string.Equals(kvp.Key, "Confidence", StringComparison.OrdinalIgnoreCase))
            {
                rawConfidence = kvp.Value;
            }
        }

        if (rawAgentResponse != null
            && TryDeserializePayloadValue(rawAgentResponse, out AgentResponse? serializedAgentResponse)
            && serializedAgentResponse != null)
        {
            response = serializedAgentResponse;
            response.AgentId = AddSourceToAgentId(response.AgentId, sourceInstanceId);
            response.Reasoning = AddSourceToReasoning(response.Reasoning, sourceInstanceId);
            return true;
        }

        decision ??= responseField;
        if (string.IsNullOrWhiteSpace(decision))
        {
            return false;
        }

        _ = TryConvertPayloadToDouble(rawConfidence, out var confidence);

        response = new AgentResponse
        {
            AgentId = AddSourceToAgentId(rawAgentId ?? "remote-agent", sourceInstanceId),
            Response = decision ?? string.Empty,
            Confidence = confidence,
            Reasoning = AddSourceToReasoning(reasoning ?? "Cross-instance response", sourceInstanceId),
            Timestamp = DateTime.UtcNow
        };

        return true;
    }

    private async Task<CollaborationResult> ExecuteLocalCollaborationFallbackAsync(
        CollaborationRequest request,
        List<AgentResponse> remoteResponses,
        string reasonCode,
        string reasonDescription,
        CancellationToken ct)
    {
        if (_localCollaborationService is null)
        {
            return new CollaborationResult
            {
                Decision = "AUTONOMOUS_LOCAL_FALLBACK_UNAVAILABLE",
                Confidence = 0,
                Responses = remoteResponses,
                ParticipantCount = remoteResponses.Count,
                AgreementScore = 0,
                AggregatedReasoning =
                    $"Autonomous federation fallback could not execute local collaboration because no local collaboration service is configured ({reasonCode}). {reasonDescription}",
                Strategy = request.Strategy,
                ConflictClass = "unresolved",
                ConflictReasonCode = "local_fallback_service_unavailable",
                NextAction = "none",
                NeedsSupervisorIntervention = false
            };
        }

        try
        {
            var localRequest = new CollaborationRequest
            {
                Id = $"{request.Id}-local-{Guid.NewGuid():N}",
                InitiatorAgentId = request.InitiatorAgentId,
                Task = request.Task,
                Strategy = request.Strategy,
                MinimumConfidence = request.MinimumConfidence,
                MinimumParticipants = request.MinimumParticipants,
                ParticipantAgentIds = request.ParticipantAgentIds.ToList(),
                Timeout = request.Timeout
            };

            var localResult = await _localCollaborationService.RequestCollaborationAsync(localRequest, ct).ConfigureAwait(false);
            localResult.AggregatedReasoning =
                $"Federation fallback executed local collaboration after {reasonCode}: {reasonDescription} | local_request_id={localRequest.Id} | {localResult.AggregatedReasoning}";
            localResult.NextAction = "none";
            localResult.NeedsSupervisorIntervention = false;
            return localResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local collaboration fallback failed for request {RequestId} ({ReasonCode})", request.Id, reasonCode);
            return new CollaborationResult
            {
                Decision = "AUTONOMOUS_LOCAL_FALLBACK_FAILED",
                Confidence = 0,
                Responses = remoteResponses,
                ParticipantCount = remoteResponses.Count,
                AgreementScore = 0,
                AggregatedReasoning =
                    $"Federation fallback attempted local collaboration after {reasonCode} but execution failed: {ex.Message}",
                Strategy = request.Strategy,
                ConflictClass = "unresolved",
                ConflictReasonCode = "local_fallback_execution_failed",
                NextAction = "none",
                NeedsSupervisorIntervention = false
            };
        }
    }

    private List<FederatedInstance> GetActiveRemoteInstancesSnapshot()
    {
        var now = DateTime.UtcNow;
        var instances = new List<FederatedInstance>(_instances.Count);

        foreach (var instance in _instances.Values)
        {
            if (!instance.IsActive)
            {
                continue;
            }

            if ((now - instance.LastHeartbeat).TotalSeconds >= 60)
            {
                continue;
            }

            if (string.Equals(instance.InstanceId, _localInstanceId, StringComparison.Ordinal))
            {
                continue;
            }

            instances.Add(instance);
        }

        return instances;
    }

    private static string? ConvertPayloadToString(object? raw)
    {
        if (raw is null)
        {
            return null;
        }

        return raw switch
        {
            string str => str,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString(),
            _ => raw.ToString(),
        };
    }

    private static bool TryConvertPayloadToDouble(object? raw, out double value)
    {
        value = 0;
        if (raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case decimal m:
                value = (double)m;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.Number:
                return json.TryGetDouble(out value);
            case JsonElement json when json.ValueKind == JsonValueKind.String:
                return double.TryParse(json.GetString(), out value);
            default:
                return double.TryParse(raw.ToString(), out value);
        }
    }

    private static bool TryDeserializePayloadValue<T>(object? raw, out T? value)
    {
        value = default;
        if (raw is null)
        {
            return false;
        }

        try
        {
            switch (raw)
            {
                case T typed:
                    value = typed;
                    return true;
                case JsonElement json:
                    value = json.Deserialize<T>(CaseInsensitiveJson);
                    return value != null;
                case string str when !string.IsNullOrWhiteSpace(str):
                    value = JsonSerializer.Deserialize<T>(str, CaseInsensitiveJson);
                    return value != null;
                default:
                    var serialized = JsonSerializer.Serialize(raw);
                    value = JsonSerializer.Deserialize<T>(serialized, CaseInsensitiveJson);
                    return value != null;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string AddSourceToReasoning(string reasoning, string sourceInstanceId)
    {
        if (reasoning.Contains("source_instance=", StringComparison.OrdinalIgnoreCase))
        {
            return reasoning;
        }

        return $"{reasoning} | source_instance={sourceInstanceId}";
    }

    private static string AddSourceToAgentId(string agentId, string sourceInstanceId)
    {
        if (agentId.Contains(':', StringComparison.Ordinal))
        {
            return agentId;
        }

        return $"{sourceInstanceId}:{agentId}";
    }

    private static string? TryReadSourceInstanceFromReasoning(string reasoning)
    {
        const string marker = "source_instance=";
        var markerIndex = reasoning.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var start = markerIndex + marker.Length;
        if (start >= reasoning.Length)
        {
            return null;
        }

        var suffix = reasoning[start..];
        var separator = suffix.IndexOfAny(['|', ';', ',', ' ']);
        return separator < 0 ? suffix.Trim() : suffix[..separator].Trim();
    }

    /// <inheritdoc/>
    public async Task SyncMemoryAsync(
        List<Fact> entries,
        List<string>? targetInstances = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Syncing {Count} memory entries to federated instances", entries.Count);

        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);
        var targets = targetInstances == null
            ? instances.Where(i => i.InstanceId != _localInstanceId).ToList()
            : instances.Where(i => targetInstances.Contains(i.InstanceId)).ToList();

        var tasks = targets.Select(instance =>
        {
            var message = new FederatedMessage
            {
                SourceInstanceId = _localInstanceId,
                TargetInstanceId = instance.InstanceId,
                MessageType = FederatedMessageType.MemorySync,
                Payload = new Dictionary<string, object>
                {
                    ["Entries"] = JsonSerializer.Serialize(entries)
                }
            };
            return SendMessageAsync(message, ct);
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task MonitorInstanceHealthAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Monitoring health of {Count} federated instances", _instances.Count);

        var instances = _instances.Values.ToList();
        var tasks = instances.Select(async instance =>
        {
            try
            {
                var message = new FederatedMessage
                {
                    SourceInstanceId = _localInstanceId,
                    TargetInstanceId = instance.InstanceId,
                    MessageType = FederatedMessageType.Heartbeat,
                    RequiresResponse = true,
                    TtlSeconds = 10
                };

                var heartbeatResponse = await SendMessageWithOptionalResponseAsync(message, ct).ConfigureAwait(false);
                if (heartbeatResponse is null)
                {
                    instance.IsActive = false;
                    return;
                }

                instance.LastHeartbeat = DateTime.UtcNow;
                instance.IsActive = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Instance {InstanceId} failed heartbeat", instance.InstanceId);
                instance.IsActive = false;
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Remove instances that haven't responded in 5 minutes
        var staleInstances = _instances.Values
            .Where(i => (DateTime.UtcNow - i.LastHeartbeat).TotalMinutes > 5)
            .ToList();

        foreach (var stale in staleInstances)
        {
            _logger.LogWarning("Removing stale instance {InstanceId}", stale.InstanceId);
            _instances.TryRemove(stale.InstanceId, out _);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> SelectInstanceForAgentAsync(CancellationToken ct = default)
    {
        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);

        var selectedInstance = instances
            .Where(i => i.CurrentAgentCount < i.MaxAgents)
            .OrderBy(i => i.CurrentLoad)
            .ThenBy(i => i.CurrentAgentCount)
            .FirstOrDefault();

        if (selectedInstance != null)
        {
            _logger.LogInformation(
                "Selected instance {InstanceId} for agent creation (load: {Load:P0}, agents: {Count}/{Max})",
                selectedInstance.InstanceId,
                selectedInstance.CurrentLoad,
                selectedInstance.CurrentAgentCount,
                selectedInstance.MaxAgents);
        }

        return selectedInstance?.InstanceId;
    }

    private sealed record ResponseGroup(
        string Response,
        int Count,
        double AverageConfidence,
        AgentResponse BestResponse,
        double WeightSum);

    private sealed class ResponseAccumulator
    {
        private readonly string _response;
        private int _count;
        private double _confidenceSum;
        private double _weightSum;
        private AgentResponse? _best;

        public ResponseAccumulator(string response)
        {
            _response = response;
        }

        public void Add(AgentResponse response)
        {
            _count++;
            _confidenceSum += response.Confidence;
            _weightSum += response.Weight > 0 ? response.Weight : Math.Max(response.Confidence, 0.1);

            if (_best is null || response.Confidence > _best.Confidence)
            {
                _best = response;
            }
        }

        public ResponseGroup ToGroup()
        {
            var average = _count == 0 ? 0 : _confidenceSum / _count;

            return new ResponseGroup(
                Response: _response,
                Count: _count,
                AverageConfidence: average,
                BestResponse: _best ?? new AgentResponse(),
                WeightSum: _weightSum);
        }
    }

    private sealed record StrategyAggregationOutcome(
        bool IsResolved,
        string Decision,
        double Confidence,
        double AgreementScore,
        AgentResponse? WinningResponse,
        string Reasoning,
        string ReasonCode)
    {
        public static StrategyAggregationOutcome Resolved(
            string decision,
            double confidence,
            double agreementScore,
            AgentResponse? winningResponse,
            string reasoning)
        {
            return new StrategyAggregationOutcome(
                IsResolved: true,
                Decision: decision,
                Confidence: confidence,
                AgreementScore: agreementScore,
                WinningResponse: winningResponse,
                Reasoning: reasoning,
                ReasonCode: string.Empty);
        }

        public static StrategyAggregationOutcome Unresolved(
            string reasonCode,
            string reasoning,
            double confidence = 0,
            double agreementScore = 0)
        {
            return new StrategyAggregationOutcome(
                IsResolved: false,
                Decision: string.Empty,
                Confidence: confidence,
                AgreementScore: agreementScore,
                WinningResponse: null,
                Reasoning: reasoning,
                ReasonCode: reasonCode);
        }
    }
}
