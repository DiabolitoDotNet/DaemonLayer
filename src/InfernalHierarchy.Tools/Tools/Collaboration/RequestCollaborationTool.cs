using System.Text.Json;
using InfernalHierarchy.Core.Serialization;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Tools.Tools.Collaboration;

/// <summary>
/// Tool for requesting collaboration and consensus from multiple agents
/// </summary>
public class RequestCollaborationTool : ITool
{
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
        "min_participants (int, default: 2), min_confidence (double, default: 0.7), participant_ranks (comma-separated ranks, optional).";

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

            // Extract strategy
            var strategyStr = parameters.GetValueOrDefault("strategy", "weighted")?.ToString()?.ToLowerInvariant() ?? "weighted";
            var strategy = strategyStr switch
            {
                "voting" => CollaborationStrategy.Voting,
                "consensus" => CollaborationStrategy.Consensus,
                "highest_confidence" => CollaborationStrategy.HighestConfidence,
                "hierarchical" => CollaborationStrategy.Hierarchical,
                _ => CollaborationStrategy.WeightedVoting
            };

            // Extract min participants
            var minParticipants = 2;
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
            var minConfidence = 0.7;
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

            // Extract initiator agent ID from parameters or context
            var initiatorAgentId = parameters.GetValueOrDefault("agent_id", "system")?.ToString() ?? "system";

            // Determine participant agents
            var participants = new List<string>();
            if (parameters.TryGetValue("participant_ranks", out var ranksObj))
            {
                var ranksStr = ranksObj?.ToString() ?? string.Empty;
                var ranks = ranksStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
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
                        participants.AddRange(agentsOfRank.Select(a => a.Id));
                    }
                }
            }

            // If no specific participants, get all active agents (excluding initiator)
            if (participants.Count == 0)
            {
                var allAgents = _agentRegistry.GetAllAgents();
                participants = allAgents
                    .Where(a => a.Status == AgentStatus.Idle || a.Status == AgentStatus.Thinking)
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
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Request collaboration
            var result = await _collaborationService.RequestCollaborationAsync(request, ct);

            // Format result
            var resultJson = JsonSerializer.Serialize(new
            {
                decision = result.Decision,
                confidence = Math.Round(result.Confidence, 2),
                agreement_score = Math.Round(result.AgreementScore, 2),
                participant_count = result.ParticipantCount,
                strategy = result.Strategy.ToString(),
                reasoning = result.AggregatedReasoning.Length > 500 
                    ? result.AggregatedReasoning[..500] + "..." 
                    : result.AggregatedReasoning
            }, JsonDefaults.WebIndented);

            return new ToolResult
            {
                Success = true,
                Output = $"Collaboration completed:\n{resultJson}"
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
}
