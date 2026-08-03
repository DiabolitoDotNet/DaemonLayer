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
    {
        _logger = logger;
        _httpClient = httpClient;
        _localInstanceId = localInstanceId;
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

        // Select instance with lowest load
        var targetInstance = instances
            .Where(i => i.InstanceId != _localInstanceId)
            .Where(i => i.CurrentAgentCount < i.MaxAgents)
            .OrderBy(i => i.CurrentLoad)
            .FirstOrDefault();

        if (targetInstance == null)
        {
            _logger.LogWarning("No available instances for task delegation");
            return null;
        }

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

        await SendMessageAsync(message, ct).ConfigureAwait(false);
        return targetInstance.InstanceId;
    }

    /// <inheritdoc/>
    public async Task<CollaborationResult> RequestCrossInstanceCollaborationAsync(
        CollaborationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting cross-instance collaboration for task: {Task}", request.Task);

        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);
        var responses = new ConcurrentBag<AgentResponse>();

        var tasks = instances
            .Where(i => i.InstanceId != _localInstanceId)
            .Select(async instance =>
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
                        responses.Add(response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get collaboration response from {InstanceId}",
                        instance.InstanceId);
                }
            });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (responses.IsEmpty)
        {
            return new CollaborationResult
            {
                Decision = "NO_CROSS_INSTANCE_RESPONSE",
                Confidence = 0,
                Responses = [],
                ParticipantCount = 0,
                AgreementScore = 0,
                AggregatedReasoning = "No remote collaboration response received from active instances.",
                Strategy = request.Strategy,
                ConflictClass = "unresolved",
                ConflictReasonCode = "cross_instance_no_responses",
                NextAction = "fallback_to_local_collaboration",
                NeedsSupervisorIntervention = false
            };
        }

        var collectedResponses = responses.ToList();
        if (collectedResponses.Count < request.MinimumParticipants)
        {
            return new CollaborationResult
            {
                Decision = "INSUFFICIENT_CROSS_INSTANCE_PARTICIPANTS",
                Confidence = 0,
                Responses = collectedResponses,
                ParticipantCount = collectedResponses.Count,
                AgreementScore = 0,
                AggregatedReasoning = $"Received {collectedResponses.Count} responses but minimum participants is {request.MinimumParticipants}.",
                Strategy = request.Strategy,
                ConflictClass = "unresolved",
                ConflictReasonCode = "cross_instance_minimum_participants_not_met",
                NextAction = "fallback_to_local_collaboration",
                NeedsSupervisorIntervention = false
            };
        }

        var strategyOutcome = AggregateByStrategy(request.Strategy, collectedResponses, request.MinimumConfidence);
        if (!strategyOutcome.IsResolved)
        {
            return new CollaborationResult
            {
                Decision = "CONFLICT_UNRESOLVED",
                Confidence = strategyOutcome.Confidence,
                Responses = collectedResponses,
                ParticipantCount = collectedResponses.Count,
                AgreementScore = strategyOutcome.AgreementScore,
                AggregatedReasoning = strategyOutcome.Reasoning,
                Strategy = request.Strategy,
                ConflictClass = "unresolved",
                ConflictReasonCode = strategyOutcome.ReasonCode,
                NextAction = "supervisor_adjudication_workflow",
                NeedsSupervisorIntervention = true
            };
        }

        var sourceInstances = collectedResponses
            .Select(r => TryReadSourceInstanceFromReasoning(r.Reasoning))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

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
        var ordered = groups
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.AverageConfidence)
            .ToList();

        if (ordered.Count == 0)
        {
            return StrategyAggregationOutcome.Unresolved("cross_instance_empty_responses", "No responses to aggregate.");
        }

        if (ordered.Count > 1
            && ordered[0].Count == ordered[1].Count
            && Math.Abs(ordered[0].AverageConfidence - ordered[1].AverageConfidence) < 0.0001)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_voting_tie",
                "Voting resulted in a tie across candidate decisions.");
        }

        var winner = ordered[0];
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
        var weighted = groups
            .Select(g => new
            {
                Group = g,
                Weight = g.Items.Sum(r => r.Weight > 0 ? r.Weight : Math.Max(r.Confidence, 0.1)),
            })
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.Group.AverageConfidence)
            .ToList();

        if (weighted.Count == 0)
        {
            return StrategyAggregationOutcome.Unresolved("cross_instance_empty_responses", "No responses to aggregate.");
        }

        if (weighted.Count > 1 && Math.Abs(weighted[0].Weight - weighted[1].Weight) < 0.0001)
        {
            return StrategyAggregationOutcome.Unresolved(
                "cross_instance_weighted_tie",
                "Weighted voting resulted in equal scores.");
        }

        var winner = weighted[0].Group;
        var confidence = winner.AverageConfidence;
        var totalWeight = weighted.Sum(x => x.Weight);
        var agreement = totalWeight <= 0 ? 0 : weighted[0].Weight / totalWeight;

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
        var groups = GroupByResponse(responses)
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.AverageConfidence)
            .ToList();

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
        var winner = responses
            .OrderByDescending(r => r.Confidence)
            .FirstOrDefault();

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
        var winner = responses
            .OrderBy(r => RankPriority(r.AgentRank))
            .ThenByDescending(r => r.Confidence)
            .FirstOrDefault();

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

    private static List<ResponseGroup> GroupByResponse(List<AgentResponse> responses)
    {
        return responses
            .Where(r => !string.IsNullOrWhiteSpace(r.Response))
            .GroupBy(r => r.Response.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ResponseGroup(
                Response: g.Key,
                Count: g.Count(),
                AverageConfidence: g.Average(x => x.Confidence),
                BestResponse: g.OrderByDescending(x => x.Confidence).First(),
                Items: g.ToList()))
            .ToList();
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

        if (TryGetPayloadValue(responseMessage.Payload, "AgentResponse", out var rawAgentResponse)
            && TryDeserializePayloadValue(rawAgentResponse, out AgentResponse? serializedAgentResponse)
            && serializedAgentResponse != null)
        {
            response = serializedAgentResponse;
            response.AgentId = AddSourceToAgentId(response.AgentId, sourceInstanceId);
            response.Reasoning = AddSourceToReasoning(response.Reasoning, sourceInstanceId);
            return true;
        }

        if (!TryGetPayloadString(responseMessage.Payload, "Decision", out var decision)
            && !TryGetPayloadString(responseMessage.Payload, "Response", out decision))
        {
            return false;
        }

        TryGetPayloadString(responseMessage.Payload, "AgentId", out var rawAgentId);
        TryGetPayloadDouble(responseMessage.Payload, "Confidence", out var confidence);
        TryGetPayloadString(responseMessage.Payload, "Reasoning", out var reasoning);

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

    private static bool TryGetPayloadValue(Dictionary<string, object> payload, string key, out object? value)
    {
        foreach (var kvp in payload)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
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

    private static bool TryGetPayloadString(Dictionary<string, object> payload, string key, out string? value)
    {
        value = null;
        if (!TryGetPayloadValue(payload, key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case string str:
                value = str;
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.String:
                value = json.GetString();
                return !string.IsNullOrWhiteSpace(value);
            default:
                value = raw.ToString();
                return !string.IsNullOrWhiteSpace(value);
        }
    }

    private static bool TryGetPayloadDouble(Dictionary<string, object> payload, string key, out double value)
    {
        value = 0;
        if (!TryGetPayloadValue(payload, key, out var raw) || raw is null)
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
        List<AgentResponse> Items);

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
