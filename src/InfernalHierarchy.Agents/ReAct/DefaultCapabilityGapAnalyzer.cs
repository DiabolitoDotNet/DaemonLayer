using System.Text.RegularExpressions;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultCapabilityGapAnalyzer : ICapabilityGapAnalyzer
{
    private sealed record CapabilityRule(
        string Capability,
        string RequiredTool,
        string ReasonCode,
        string Description,
        Regex[] Matchers,
        string? PreferredProfile = null);

    private static readonly CapabilityRule[] Rules =
    {
        new(
            Capability: "http_api_integration",
            RequiredTool: "http_request",
            ReasonCode: "missing_http_tool",
            Description: "Task requires HTTP/API calls.",
            Matchers:
            [
                new Regex(@"\b(api|endpoint|rest|http|webhook)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            PreferredProfile: "Build"),
        new(
            Capability: "graphql_access",
            RequiredTool: "graphql_request",
            ReasonCode: "missing_graphql_tool",
            Description: "Task requires GraphQL querying.",
            Matchers:
            [
                new Regex(@"\bgraphql\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            PreferredProfile: "Build"),
        new(
            Capability: "sql_read",
            RequiredTool: "sql_query_readonly",
            ReasonCode: "missing_sql_tool",
            Description: "Task requires SQL read access.",
            Matchers:
            [
                new Regex(@"\b(sql|database query|select\s+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            PreferredProfile: "Build"),
        new(
            Capability: "vision_analysis",
            RequiredTool: "vision_describe",
            ReasonCode: "missing_vision_tool",
            Description: "Task requires image understanding.",
            Matchers:
            [
                new Regex(@"\b(image|screenshot|vision|picture|photo)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ]),
        new(
            Capability: "audio_transcription",
            RequiredTool: "audio_transcribe",
            ReasonCode: "missing_audio_tool",
            Description: "Task requires audio transcription.",
            Matchers:
            [
                new Regex(@"\b(audio|transcribe|voice)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ]),
        new(
            Capability: "agent_collaboration",
            RequiredTool: "request_collaboration",
            ReasonCode: "missing_collaboration_tool",
            Description: "Task requires delegation/collaboration.",
            Matchers:
            [
                new Regex(@"\b(delegate|collaborat|parallel agents?|sub-?agent)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            PreferredProfile: "Research")
    };

    private readonly ISkillPackCatalog? _skillPackCatalog;

    public DefaultCapabilityGapAnalyzer(ISkillPackCatalog? skillPackCatalog = null)
    {
        _skillPackCatalog = skillPackCatalog;
    }

    public async Task<CapabilityGapAnalysisResult> AnalyzeAsync(
        ReActTaskProcessorContext context,
        AgentMessage task,
        Persona effectivePersona,
        CancellationToken ct)
    {
        var content = task.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return new CapabilityGapAnalysisResult(Array.Empty<CapabilityGap>(), Array.Empty<CapabilityRemediationAction>());
        }

        var allowedTools = new HashSet<string>(effectivePersona.AvailableTools, StringComparer.OrdinalIgnoreCase);
        var profileAllowedTools = TryReadProfileAllowedTools(task.Payload);
        var executionProfile = TryGetValue(task.Payload, "execution_profile") ?? TryGetValue(task.Payload, "profile");

        var gaps = new List<CapabilityGap>();
        foreach (var rule in Rules)
        {
            if (!MatchesAny(rule.Matchers, content))
            {
                continue;
            }

            var hasTool = allowedTools.Contains(rule.RequiredTool);
            var blockedByProfile = profileAllowedTools is not null && !profileAllowedTools.Contains(rule.RequiredTool);

            if (hasTool && !blockedByProfile)
            {
                continue;
            }

            var suggestedSkillPackId = await FindSkillPackForToolAsync(rule.RequiredTool, ct).ConfigureAwait(false);

            gaps.Add(new CapabilityGap(
                Capability: rule.Capability,
                ReasonCode: blockedByProfile ? "profile_constraint_blocked_tool" : rule.ReasonCode,
                Description: rule.Description,
                BlockedByProfile: blockedByProfile,
                SuggestedSkillPackId: suggestedSkillPackId,
                SuggestedExecutionProfile: rule.PreferredProfile));
        }

        var remediations = BuildRemediations(gaps, allowedTools, executionProfile);

        return new CapabilityGapAnalysisResult(gaps, remediations);
    }

    private static IReadOnlyList<CapabilityRemediationAction> BuildRemediations(
        IReadOnlyList<CapabilityGap> gaps,
        HashSet<string> allowedTools,
        string? executionProfile)
    {
        var remediations = new List<CapabilityRemediationAction>();

        foreach (var gap in gaps)
        {
            if (gap.BlockedByProfile)
            {
                remediations.Add(new CapabilityRemediationAction(
                    Kind: CapabilityRemediationActionKind.SwitchExecutionProfile,
                    ReasonCode: "switch_profile_required",
                    Capability: gap.Capability,
                    Description: $"Current execution profile blocks required capability '{gap.Capability}'.",
                    TargetExecutionProfile: gap.SuggestedExecutionProfile ?? executionProfile ?? "Build"));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(gap.SuggestedSkillPackId) && allowedTools.Contains("request_skill_pack"))
            {
                remediations.Add(new CapabilityRemediationAction(
                    Kind: CapabilityRemediationActionKind.RequestSkillPack,
                    ReasonCode: "request_skill_pack",
                    Capability: gap.Capability,
                    Description: $"Request skill pack '{gap.SuggestedSkillPackId}' to unlock '{gap.Capability}'.",
                    SkillPackId: gap.SuggestedSkillPackId));
                continue;
            }

            if (allowedTools.Contains("create_custom_tool"))
            {
                var toolName = $"custom_{NormalizeIdentifier(gap.Capability)}";
                remediations.Add(new CapabilityRemediationAction(
                    Kind: CapabilityRemediationActionKind.CreateCustomTool,
                    ReasonCode: "synthesize_custom_tool",
                    Capability: gap.Capability,
                    Description: $"Synthesize custom tool to cover '{gap.Capability}'.",
                    CustomToolName: toolName,
                    CustomToolRequirement: BuildRequirement(gap, toolName)));
                continue;
            }

            remediations.Add(new CapabilityRemediationAction(
                Kind: CapabilityRemediationActionKind.EscalateCollaboration,
                ReasonCode: "escalate_collaboration",
                Capability: gap.Capability,
                Description: $"Escalate for collaboration support on '{gap.Capability}'."));
        }

        return remediations;
    }

    private static string BuildRequirement(CapabilityGap gap, string toolName)
    {
        return $"Create a safe tool named '{toolName}' to satisfy capability '{gap.Capability}'. {gap.Description} It must accept structured parameters and return concise deterministic output.";
    }

    private async Task<string?> FindSkillPackForToolAsync(string requiredTool, CancellationToken ct)
    {
        if (_skillPackCatalog is null)
        {
            return null;
        }

        try
        {
            var packs = await _skillPackCatalog.GetAllAsync(ct).ConfigureAwait(false);
            var match = packs.FirstOrDefault(p =>
                p.Enabled
                && p.AdditionalTools.Any(t => string.Equals(t, requiredTool, StringComparison.OrdinalIgnoreCase)));

            return match?.Id;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesAny(IEnumerable<Regex> matchers, string content)
    {
        foreach (var matcher in matchers)
        {
            if (matcher.IsMatch(content))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string>? TryReadProfileAllowedTools(Dictionary<string, object>? payload)
    {
        var raw = TryGetValue(payload, "profile_allowed_tools") ?? TryGetValue(payload, "allowed_tools");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var items = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryGetValue(Dictionary<string, object>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw.ToString()?.Trim();
    }

    private static string NormalizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "generated";
        }

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();

        var normalized = new string(chars);
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return normalized.Trim('_');
    }
}
