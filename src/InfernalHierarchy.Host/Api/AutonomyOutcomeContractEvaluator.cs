namespace InfernalHierarchy.Host.Api;

internal static class AutonomyOutcomeContractEvaluator
{
    public static Dictionary<string, object> BuildTimeoutOutcomePayload()
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["autonomy_outcome_status"] = "timeout",
            ["autonomy_outcome_reason_code"] = "playground_timeout",
            ["autonomy_outcome_autonomous_success"] = false,
            ["autonomy_outcome_needs_supervisor_intervention"] = false,
            ["autonomy_outcome_next_action"] = "none"
        };
    }

    public static Dictionary<string, object> EnrichAutonomyOutcomePayload(string content, Dictionary<string, object>? payload)
    {
        var enriched = new Dictionary<string, object>(payload ?? new Dictionary<string, object>(), StringComparer.OrdinalIgnoreCase);

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

        return enriched;
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