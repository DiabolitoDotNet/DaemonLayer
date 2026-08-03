namespace InfernalHierarchy.Host.Api;

internal static class AutonomyOutcomeContractEvaluator
{
    private static readonly string[] RequiredOutcomeKeys =
    [
        "autonomy_outcome_status",
        "autonomy_outcome_reason_code",
        "autonomy_outcome_autonomous_success",
        "autonomy_outcome_needs_supervisor_intervention",
        "autonomy_outcome_next_action"
    ];

    public static Dictionary<string, object> BuildTimeoutOutcomePayload()
    {
        return new Dictionary<string, object>(capacity: 8, comparer: StringComparer.OrdinalIgnoreCase)
        {
            ["autonomy_outcome_status"] = "timeout",
            ["autonomy_outcome_reason_code"] = "playground_timeout",
            ["autonomy_outcome_autonomous_success"] = false,
            ["autonomy_outcome_needs_supervisor_intervention"] = false,
            ["autonomy_outcome_next_action"] = "none",
            ["autonomy_scope_classification"] = "in_scope_autonomous",
            ["autonomy_scope_reason_code"] = "in_scope_runtime_timeout",
            ["autonomy_out_of_scope"] = false
        };
    }

    public static Dictionary<string, object> EnrichAutonomyOutcomePayload(string content, Dictionary<string, object>? payload)
    {
        var seedCapacity = (payload?.Count ?? 0) + 10;
        var enriched = new Dictionary<string, object>(seedCapacity, StringComparer.OrdinalIgnoreCase);
        if (payload is not null)
        {
            foreach (var entry in payload)
            {
                enriched[entry.Key] = entry.Value;
            }
        }

        var isTimeout = content.StartsWith("Timeout:", StringComparison.OrdinalIgnoreCase);
        var capabilityGapState = TryGetString(enriched, "capability_gap_state");
        var nextAction = TryGetString(enriched, "next_action") ?? "none";
        var needsSupervisorIntervention = TryGetBool(enriched, "needs_supervisor_intervention");
        var terminalReasonCode =
            TryGetString(enriched, "capability_gap_terminal_reason_code")
            ?? TryGetString(enriched, "terminal_reason_code")
            ?? string.Empty;

        var autonomyBlocked = string.Equals(capabilityGapState, "capability_gap_policy_blocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityGapState, "capability_gap_unresolved_terminal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityGapState, "blocked_by_sensitive_input_guard", StringComparison.OrdinalIgnoreCase);

        var scopeClassification = string.Equals(capabilityGapState, "blocked_by_sensitive_input_guard", StringComparison.OrdinalIgnoreCase)
            ? "out_of_scope_requires_secret_ref"
            : string.Equals(capabilityGapState, "capability_gap_policy_blocked", StringComparison.OrdinalIgnoreCase)
                ? "out_of_scope_policy_blocked"
                : "in_scope_autonomous";

        var scopeReasonCode = scopeClassification switch
        {
            "out_of_scope_requires_secret_ref" => "sensitive_input_requires_secret_reference",
            "out_of_scope_policy_blocked" => terminalReasonCode,
            _ => "in_scope"
        };

        var nonAutonomousTerminal = needsSupervisorIntervention
            || !string.Equals(nextAction, "none", StringComparison.OrdinalIgnoreCase);

        var autonomousSuccess = !isTimeout && !autonomyBlocked && !nonAutonomousTerminal;
        var outcomeStatus = isTimeout
            ? "timeout"
            : autonomyBlocked
                ? "autonomy_blocked"
                : nonAutonomousTerminal
                    ? "non_autonomous_terminal"
                    : "success";

        enriched["autonomy_outcome_status"] = outcomeStatus;
        enriched["autonomy_outcome_reason_code"] = string.IsNullOrWhiteSpace(terminalReasonCode)
            ? outcomeStatus
            : terminalReasonCode;
        enriched["autonomy_outcome_autonomous_success"] = autonomousSuccess;
        enriched["autonomy_outcome_needs_supervisor_intervention"] = needsSupervisorIntervention;
        enriched["autonomy_outcome_next_action"] = nextAction;
        enriched["autonomy_scope_classification"] = scopeClassification;
        enriched["autonomy_scope_reason_code"] = string.IsNullOrWhiteSpace(scopeReasonCode) ? "out_of_scope_policy_blocked" : scopeReasonCode;
        enriched["autonomy_out_of_scope"] = !string.Equals(scopeClassification, "in_scope_autonomous", StringComparison.OrdinalIgnoreCase);

        return enriched;
    }

    public static bool HasRequiredOutcomeContract(Dictionary<string, object>? payload, out IReadOnlyList<string> missingKeys)
    {
        var missing = new List<string>();
        if (payload is null)
        {
            missing.AddRange(RequiredOutcomeKeys);
            missingKeys = missing;
            return false;
        }

        foreach (var key in RequiredOutcomeKeys)
        {
            if (!payload.TryGetValue(key, out var value) || value is null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                missing.Add(key);
            }
        }

        missingKeys = missing;
        return missing.Count == 0;
    }

    private static string? TryGetString(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is string text)
        {
            return text;
        }

        return raw.ToString();
    }

    private static bool TryGetBool(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is bool flag)
        {
            return flag;
        }

        if (raw is string text && bool.TryParse(text, out flag))
        {
            return flag;
        }

        return false;
    }
}