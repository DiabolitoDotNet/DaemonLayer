using System.Text.Json;

namespace InfernalHierarchy.Tools.Tools.Collaboration;

/// <summary>
/// Tool for requesting collaboration and consensus from multiple agents
/// </summary>
public class RequestCollaborationTool : ITool
{
    private sealed record CollaborationTemplateDefinition(
        string Name,
        CollaborationStrategy Strategy,
        int MinParticipants,
        double MinConfidence,
        string ParticipantRanks,
        bool IncludeThinking,
        string Description);

    private static readonly IReadOnlyDictionary<string, CollaborationTemplateDefinition> Templates =
        new Dictionary<string, CollaborationTemplateDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["parallel_research_adjudicate"] = new(
                Name: "parallel_research_adjudicate",
                Strategy: CollaborationStrategy.WeightedVoting,
                MinParticipants: 3,
                MinConfidence: 0.75,
                ParticipantRanks: "worker,duke",
                IncludeThinking: false,
                Description: "Run parallel research by workers, then weighted adjudication."),
            ["debate_then_synthesize"] = new(
                Name: "debate_then_synthesize",
                Strategy: CollaborationStrategy.Consensus,
                MinParticipants: 2,
                MinConfidence: 0.8,
                ParticipantRanks: "worker,duke",
                IncludeThinking: true,
                Description: "Collect opposing proposals, then converge through synthesis rounds."),
            ["hierarchical_risk_review"] = new(
                Name: "hierarchical_risk_review",
                Strategy: CollaborationStrategy.Hierarchical,
                MinParticipants: 2,
                MinConfidence: 0.7,
                ParticipantRanks: "prince,duke,worker",
                IncludeThinking: false,
                Description: "Use hierarchical override for high-risk or high-impact decisions.")
        };

    private readonly ILogger<RequestCollaborationTool> _logger;
    private readonly IAgentCollaborationService _collaborationService;
    private readonly IAgentRegistry _agentRegistry;

    public RequestCollaborationTool(
        ILogger<RequestCollaborationTool> logger,
        IAgentCollaborationService collaborationService,
        IAgentRegistry agentRegistry)
    {
        _logger = logger;
        _collaborationService = collaborationService;
        _agentRegistry = agentRegistry;
    }

    public string Name => "request_collaboration";

    public string Description => "Request collaboration and consensus from multiple agents for complex decisions. " +
        "Use when you need input from other agents or want to reach consensus. " +
        "Parameters: task (string, required), strategy (voting/weighted/consensus/highest_confidence/hierarchical, default: weighted), " +
        "template (parallel_research_adjudicate/debate_then_synthesize/hierarchical_risk_review, optional), " +
        "min_participants (int, default: 2), min_confidence (double, default: 0.7), participant_ranks (comma-separated ranks, optional), " +
        "timeout_seconds (int, default: 120), include_thinking (bool, default: false).";

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        try
        {
            // Extract task
            if (!parameters.TryGetValue("task", out var taskObj))
            {
                return new ToolResult
                {
                    Success = false,
                    Error = "'task' parameter is required"
                };
            }
            var task = taskObj?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(task))
            {
                return new ToolResult
                {
                    Success = false,
                    Error = "Task cannot be empty"
                };
            }

            var templateName = parameters.GetValueOrDefault("template")?.ToString();
            var template = TryGetTemplate(templateName);

            // Extract strategy
            var defaultStrategy = template?.Strategy switch
            {
                CollaborationStrategy.Voting => "voting",
                CollaborationStrategy.Consensus => "consensus",
                CollaborationStrategy.HighestConfidence => "highest_confidence",
                CollaborationStrategy.Hierarchical => "hierarchical",
                _ => "weighted"
            };

            var strategyStr = parameters.GetValueOrDefault("strategy", defaultStrategy ?? "weighted")?.ToString()?.ToLowerInvariant() ?? "weighted";
            var strategy = strategyStr switch
            {
                "voting" => CollaborationStrategy.Voting,
                "consensus" => CollaborationStrategy.Consensus,
                "highest_confidence" => CollaborationStrategy.HighestConfidence,
                "hierarchical" => CollaborationStrategy.Hierarchical,
                _ => CollaborationStrategy.WeightedVoting
            };

            // Extract min participants
            var minParticipants = template?.MinParticipants ?? 2;
            if (parameters.TryGetValue("min_participants", out var minParticipantsObj))
            {
                if (minParticipantsObj is int minParticipantsInt)
                {
                    minParticipants = minParticipantsInt;
                }
                else if (int.TryParse(minParticipantsObj?.ToString(), out var parsed))
                {
                    minParticipants = parsed;
                }
            }

            minParticipants = Math.Max(2, Math.Min(10, minParticipants)); // Clamp 2-10

            // Extract min confidence
            var minConfidence = template?.MinConfidence ?? 0.7;
            if (parameters.TryGetValue("min_confidence", out var minConfidenceObj))
            {
                if (minConfidenceObj is double minConfidenceDouble)
                {
                    minConfidence = minConfidenceDouble;
                }
                else if (double.TryParse(minConfidenceObj?.ToString(), out var parsed))
                {
                    minConfidence = parsed;
                }
            }

            minConfidence = Math.Max(0.0, Math.Min(1.0, minConfidence)); // Clamp 0-1

            // Extract collaboration timeout
            var timeoutSeconds = 120;
            if (parameters.TryGetValue("timeout_seconds", out var timeoutSecondsObj))
            {
                if (timeoutSecondsObj is int timeoutSecondsInt)
                {
                    timeoutSeconds = timeoutSecondsInt;
                }
                else if (int.TryParse(timeoutSecondsObj?.ToString(), out var parsed))
                {
                    timeoutSeconds = parsed;
                }
            }

            // Clamp: avoid tiny timeouts that always fail and huge timeouts that block forever.
            timeoutSeconds = Math.Max(5, Math.Min(600, timeoutSeconds));

            // Select participants: by default only pick Idle agents.
            var includeThinking = template?.IncludeThinking ?? false;
            if (parameters.TryGetValue("include_thinking", out var includeThinkingObj) && includeThinkingObj != null)
            {
                if (includeThinkingObj is bool includeThinkingBool)
                {
                    includeThinking = includeThinkingBool;
                }
                else if (bool.TryParse(includeThinkingObj.ToString(), out var parsed))
                {
                    includeThinking = parsed;
                }
            }

            bool IsEligible(IAgent agent)
                => agent.Status == AgentStatus.Idle || (includeThinking && agent.Status == AgentStatus.Thinking);

            // Extract initiator agent ID from parameters or context
            var initiatorAgentId = parameters.GetValueOrDefault("agent_id", "system")?.ToString() ?? "system";

            // Determine participant agents
            var participants = new List<string>();
            var rankSelector = parameters.TryGetValue("participant_ranks", out var ranksObj)
                ? ranksObj?.ToString()
                : template?.ParticipantRanks;

            if (!string.IsNullOrWhiteSpace(rankSelector))
            {
                var ranks = rankSelector.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim().ToLowerInvariant())
                    .ToList();

                foreach (var rankStr in ranks)
                {
                    var rank = rankStr switch
                    {
                        "supreme" => AgentRank.Supreme,
                        "prince" => AgentRank.Prince,
                        "duke" => AgentRank.Duke,
                        "worker" => AgentRank.Worker,
                        _ => (AgentRank?)null
                    };

                    if (rank.HasValue)
                    {
                        var agentsOfRank = _agentRegistry.GetAgentsByRank(rank.Value);
                        participants.AddRange(agentsOfRank.Where(IsEligible).Select(a => a.Id));
                    }
                }
            }

            // If no specific participants, get all active agents (excluding initiator)
            if (participants.Count == 0)
            {
                var allAgents = _agentRegistry.GetAllAgents();
                participants = allAgents
                    .Where(IsEligible)
                    .Where(a => a.Id != initiatorAgentId)
                    .Take(5) // Limit to 5 agents by default
                    .Select(a => a.Id)
                    .ToList();
            }

            if (participants.Count < minParticipants)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = $"Not enough active agents available. Found {participants.Count}, need {minParticipants}"
                };
            }

            _logger.LogInformation(
                "🤝 Initiating collaboration with {ParticipantCount} agents using {Strategy} strategy",
                participants.Count,
                strategy);

            // Create collaboration request
            var request = new CollaborationRequest
            {
                InitiatorAgentId = initiatorAgentId,
                Task = task,
                Strategy = strategy,
                MinimumConfidence = minConfidence,
                MinimumParticipants = minParticipants,
                ParticipantAgentIds = participants,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            // Request collaboration
            var result = await _collaborationService.RequestCollaborationAsync(request, ct);

            // Format result
            var resultJson = JsonSerializer.Serialize(new
            {
                collaboration_id = request.Id,
                decision = result.Decision,
                confidence = Math.Round(result.Confidence, 2),
                agreement_score = Math.Round(result.AgreementScore, 2),
                participant_count = result.ParticipantCount,
                strategy = result.Strategy.ToString(),
                template = template?.Name,
                conflict = new
                {
                    @class = result.ConflictClass,
                    reason_code = result.ConflictReasonCode,
                    next_action = result.NextAction,
                    needs_supervisor_intervention = result.NeedsSupervisorIntervention
                },
                reasoning = result.AggregatedReasoning.Length > 500 
                    ? result.AggregatedReasoning[..500] + "..." 
                    : result.AggregatedReasoning
            }, JsonDefaults.WebIndented);

            return new ToolResult
            {
                Success = true,
                Output = $"Collaboration completed:\n{resultJson}",
                Metadata =
                {
                    ["collaboration_id"] = request.Id,
                    ["template"] = template?.Name ?? string.Empty,
                    ["conflict_class"] = result.ConflictClass,
                    ["conflict_reason_code"] = result.ConflictReasonCode,
                    ["next_action"] = result.NextAction,
                    ["needs_supervisor_intervention"] = result.NeedsSupervisorIntervention
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute collaboration request");
            return new ToolResult
            {
                Success = false,
                Error = $"Collaboration failed: {ex.Message}"
            };
        }
    }

    private static CollaborationTemplateDefinition? TryGetTemplate(string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return null;
        }

        return Templates.TryGetValue(templateName.Trim(), out var template)
            ? template
            : null;
    }
}
