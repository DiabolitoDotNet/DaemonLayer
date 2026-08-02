namespace InfernalHierarchy.Agents.Policies;

/// <summary>
/// Default governance model:
/// manager baseline assignment + policy-controlled self-service temporary requests.
/// </summary>
public sealed class DefaultAgentSkillAssignmentPolicy : IAgentSkillAssignmentPolicy
{
    private readonly ISkillPackCatalog _catalog;
    private readonly AgentSkillAssignmentOptions _options;
    private readonly ILogger<DefaultAgentSkillAssignmentPolicy> _logger;

    public DefaultAgentSkillAssignmentPolicy(
        ISkillPackCatalog catalog,
        IOptions<AgentSkillAssignmentOptions> options,
        ILogger<DefaultAgentSkillAssignmentPolicy> logger)
    {
        _catalog = catalog;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> SelectInitialSkillPackIdsAsync(
        Persona persona,
        AgentRank targetRank,
        string? parentAgentId,
        CancellationToken ct = default)
    {
        if (!_options.Enabled || !_options.AutoAssignBaseSkills)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var configured = targetRank switch
        {
            AgentRank.Supreme => _options.SupremeBaseSkillPacks,
            AgentRank.Prince => _options.PrinceBaseSkillPacks,
            AgentRank.Duke => _options.DukeBaseSkillPacks,
            AgentRank.Worker => _options.WorkerBaseSkillPacks,
            _ => Array.Empty<string>()
        };

        var ids = configured
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogDebug(
            "Selected {Count} base skill packs for rank {Rank} (ParentAgentId={ParentAgentId}, Persona={PersonaName})",
            ids.Length,
            targetRank,
            parentAgentId ?? string.Empty,
            persona.Name);

        return Task.FromResult<IReadOnlyList<string>>(ids);
    }

    public async Task<SkillAssignmentDecision> EvaluateTemporarySkillRequestAsync(
        SkillAssignmentRequest request,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return SkillAssignmentDecision.Denied("policy_disabled", "Skill assignment policy is disabled.");
        }

        if (!_options.AllowSelfServiceSkillRequests)
        {
            return SkillAssignmentDecision.EscalationRequired(
                "self_service_disabled",
                "Self-service skill assignment is disabled. Escalate to manager.");
        }

        if (!IsRankAllowed(_options.SelfServiceAllowedRanks, request.RequestorRank))
        {
            return SkillAssignmentDecision.EscalationRequired(
                "rank_requires_escalation",
                $"Rank {request.RequestorRank} cannot self-assign temporary skills.");
        }

        var pack = await _catalog.GetByIdAsync(request.SkillPackId, ct);
        if (pack == null)
        {
            return SkillAssignmentDecision.Denied("skill_not_found", $"Skill pack '{request.SkillPackId}' was not found.");
        }

        if (!pack.Enabled)
        {
            return SkillAssignmentDecision.Denied("skill_disabled", $"Skill pack '{pack.Id}' is disabled.");
        }

        if (pack.AllowedRanks.Count > 0 && !IsRankAllowed(pack.AllowedRanks, request.TargetAgentRank))
        {
            return SkillAssignmentDecision.Denied(
                "target_rank_not_allowed",
                $"Skill pack '{pack.Id}' is not allowed for rank {request.TargetAgentRank}.");
        }

        if (CompareRisk(pack.RiskLevel, _options.EscalateRiskLevelAtOrAbove) >= 0)
        {
            return SkillAssignmentDecision.EscalationRequired(
                "high_risk_escalation",
                $"Skill pack '{pack.Id}' risk level '{pack.RiskLevel}' requires manager approval.");
        }

        return SkillAssignmentDecision.Approved("approved", $"Skill pack '{pack.Id}' approved.");
    }

    private static bool IsRankAllowed(IReadOnlyList<string> allowedRanks, AgentRank rank)
    {
        return allowedRanks.Any(r => string.Equals(r, rank.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static int CompareRisk(string riskLevel, string threshold)
    {
        static int Score(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "low" => 1,
                "medium" => 2,
                "high" => 3,
                "critical" => 4,
                _ => 2
            };
        }

        return Score(riskLevel) - Score(threshold);
    }
}
