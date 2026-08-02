using System.Text.Json;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Eventing;

namespace InfernalHierarchy.Tools.Tools.Agent;

/// <summary>
/// Requests temporary skill packs for an agent and applies approved grants at runtime.
/// </summary>
public sealed class RequestSkillPackTool : ITool
{
    private readonly ILogger<RequestSkillPackTool> _logger;
    private readonly ISkillPackCatalog _catalog;
    private readonly IAgentSkillAssignmentPolicy _policy;
    private readonly IAgentSkillRuntimeStore _runtimeStore;
    private readonly AgentSkillAssignmentOptions _options;
    private readonly IAgentEventSink? _eventSink;
    private readonly ICapabilityOutcomePublisher? _outcomePublisher;

    public string Name => "request_skill_pack";

    public string Description => "Request a temporary skill pack for the current or target agent. " +
        "Parameters: skill_pack_id (required), reason (required), target_agent_id (optional), ttl_minutes (optional, default 30), temporary (optional, default true).";

    public RequestSkillPackTool(
        ILogger<RequestSkillPackTool> logger,
        ISkillPackCatalog catalog,
        IAgentSkillAssignmentPolicy policy,
        IAgentSkillRuntimeStore runtimeStore,
        IOptions<AgentSkillAssignmentOptions>? options = null,
        IAgentEventSink? eventSink = null,
        ICapabilityOutcomePublisher? outcomePublisher = null)
    {
        _logger = logger;
        _catalog = catalog;
        _policy = policy;
        _runtimeStore = runtimeStore;
        _options = options?.Value ?? new AgentSkillAssignmentOptions();
        _eventSink = eventSink;
        _outcomePublisher = outcomePublisher;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!TryGetString(parameters, out var skillPackId, "skill_pack_id", "skillPackId", "skill", "skill_id"))
        {
            return Fail("Missing required parameter: skill_pack_id");
        }

        if (!TryGetString(parameters, out var reason, "reason", "justification"))
        {
            return Fail("Missing required parameter: reason");
        }

        var requestorAgentId = TryGetString(parameters, out var parsedAgentId, "agent_id", "requestor_agent_id")
            ? parsedAgentId
            : string.Empty;

        if (string.IsNullOrWhiteSpace(requestorAgentId))
        {
            return Fail("Missing required context: agent_id");
        }

        var requestorRank = ParseRank(parameters, "agent_rank", defaultValue: AgentRank.Worker);

        var targetAgentId = TryGetString(parameters, out var parsedTargetId, "target_agent_id", "targetAgentId")
            ? parsedTargetId
            : requestorAgentId;

        var targetRank = ParseRank(parameters, "target_agent_rank", defaultValue: requestorRank);

        var temporary = TryGetBoolean(parameters, out var temp, "temporary", "is_temporary") ? temp : true;

        var ttlMinutes = TryGetInt(parameters, out var ttl, "ttl_minutes", "ttlMinutes") ? ttl : 30;
        ttlMinutes = Math.Clamp(ttlMinutes, 1, 240);

        var decision = await _policy.EvaluateTemporarySkillRequestAsync(new SkillAssignmentRequest
        {
            SkillPackId = skillPackId,
            RequestorAgentId = requestorAgentId,
            RequestorRank = requestorRank,
            TargetAgentId = targetAgentId,
            TargetAgentRank = targetRank,
            Temporary = temporary,
            Reason = reason
        }, ct).ConfigureAwait(false);

        if (!decision.IsApproved
            && decision.RequiresEscalation
            && _options.AutoApproveEscalationsByMainAgent)
        {
            decision = SkillAssignmentDecision.Approved(
                reasonCode: "auto_approved_by_main_agent",
                message: $"Auto-approved by main agent '{_options.MainAgentId}' (no human approval required).");
        }

        if (!decision.IsApproved)
        {
            AppendAuditEvent(requestorAgentId, targetAgentId, skillPackId, decision, null, reason, ttlMinutes);

            var status = decision.RequiresEscalation ? "escalation_required" : "denied";
            return new ToolResult
            {
                Success = false,
                Output = $"Skill request {status}: {decision.Message}",
                Error = decision.Message,
                Metadata = new Dictionary<string, object>
                {
                    ["decision"] = status,
                    ["reason_code"] = decision.ReasonCode,
                    ["skill_pack_id"] = skillPackId,
                    ["target_agent_id"] = targetAgentId
                }
            };
        }

        var pack = await _catalog.GetByIdAsync(skillPackId, ct).ConfigureAwait(false);
        if (pack == null)
        {
            return Fail($"Skill pack '{skillPackId}' not found after approval");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes);

        _runtimeStore.ApplyGrant(targetAgentId, new AgentSkillGrant
        {
            SkillPackId = pack.Id,
            ExpiresAtUtc = expiresAt,
            AdditionalTools = pack.AdditionalTools,
            AdditionalSpecializations = pack.AdditionalSpecializations,
            PromptFragments = pack.PromptFragments
        });

        AppendAuditEvent(requestorAgentId, targetAgentId, skillPackId, decision, expiresAt, reason, ttlMinutes);

        var overlay = _runtimeStore.GetOverlay(targetAgentId, DateTime.UtcNow);

        if (_outcomePublisher is not null)
        {
            await _outcomePublisher.RecordOutcomeAsync(new CapabilityOutcome
            {
                Kind = CapabilityOutcomeKind.SkillPackGranted,
                CapabilityId = pack.Id,
                CapabilityType = "skill_pack",
                SourceTask = reason,
                RiskLevel = pack.RiskLevel,
                AgentId = requestorAgentId,
                OccurredAtUtc = DateTime.UtcNow
            }, ct).ConfigureAwait(false);
        }

        return new ToolResult
        {
            Success = true,
            Output = $"Skill pack '{pack.Id}' approved and applied to agent '{targetAgentId}' until {expiresAt:O}.",
            Metadata = new Dictionary<string, object>
            {
                ["decision"] = "approved",
                ["reason_code"] = decision.ReasonCode,
                ["skill_pack_id"] = pack.Id,
                ["target_agent_id"] = targetAgentId,
                ["expires_at_utc"] = expiresAt.ToString("O"),
                ["runtime_tools"] = JsonSerializer.Serialize(overlay.AdditionalTools),
                ["runtime_specializations"] = JsonSerializer.Serialize(overlay.AdditionalSpecializations),
                ["runtime_skill_packs"] = JsonSerializer.Serialize(overlay.ActiveSkillPackIds)
            }
        };
    }

    private void AppendAuditEvent(
        string requestorAgentId,
        string targetAgentId,
        string skillPackId,
        SkillAssignmentDecision decision,
        DateTime? expiresAtUtc,
        string reason,
        int ttlMinutes)
    {
        if (_eventSink == null)
        {
            return;
        }

        try
        {
            _eventSink.AppendEvent(new AgentEvent
            {
                AgentId = requestorAgentId,
                Type = decision.IsApproved ? EventType.ToolExecuted : EventType.ErrorOccurred,
                Description = decision.IsApproved
                    ? $"Skill pack approved: {skillPackId}"
                    : $"Skill pack request denied/escalated: {skillPackId}",
                Metadata = new Dictionary<string, object>
                {
                    ["tool"] = Name,
                    ["skill_pack_id"] = skillPackId,
                    ["target_agent_id"] = targetAgentId,
                    ["decision_reason_code"] = decision.ReasonCode,
                    ["decision_message"] = decision.Message,
                    ["requires_escalation"] = decision.RequiresEscalation,
                    ["requested_ttl_minutes"] = ttlMinutes,
                    ["expires_at_utc"] = expiresAtUtc?.ToString("O") ?? string.Empty,
                    ["request_reason"] = reason
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to append skill assignment audit event");
        }
    }

    private static ToolResult Fail(string message) => new()
    {
        Success = false,
        Error = message,
        Output = string.Empty
    };

    private static bool TryGetString(Dictionary<string, object> parameters, out string value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw?.ToString()))
            {
                value = raw!.ToString()!.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetInt(Dictionary<string, object> parameters, out int value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var raw) && raw is not null && int.TryParse(raw.ToString(), out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetBoolean(Dictionary<string, object> parameters, out bool value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var raw) && raw is not null && bool.TryParse(raw.ToString(), out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static AgentRank ParseRank(Dictionary<string, object> parameters, string key, AgentRank defaultValue)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return defaultValue;
        }

        return Enum.TryParse<AgentRank>(raw.ToString(), ignoreCase: true, out var parsed)
            ? parsed
            : defaultValue;
    }
}
