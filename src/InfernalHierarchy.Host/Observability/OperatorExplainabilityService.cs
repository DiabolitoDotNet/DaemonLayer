using InfernalHierarchy.Core.Eventing;

namespace InfernalHierarchy.Host.Observability;

internal sealed class OperatorExplainabilityService
{
    public OperatorExplainabilityReport BuildReport(IEnumerable<AgentEvent> events, int maxItems)
    {
        var capped = Math.Clamp(maxItems, 1, 2000);

        var explainable = events
            .Where(IsExplainableEvent)
            .OrderBy(e => e.Timestamp)
            .TakeLast(capped)
            .Select(MapItem)
            .ToList();

        var summary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["tool_or_skill_creation"] = explainable.Count(i => i.Kind == "tool_or_skill_creation"),
            ["deadletter_replay"] = explainable.Count(i => i.Kind == "deadletter_replay"),
            ["execution_profile_switch"] = explainable.Count(i => i.Kind == "execution_profile_switch"),
            ["branch_preempted"] = explainable.Count(i => i.Kind == "branch_preempted")
        };

        return new OperatorExplainabilityReport(explainable, summary);
    }

    private static bool IsExplainableEvent(AgentEvent evt)
    {
        var category = ReadMetadata(evt, "category");
        if (string.Equals(category, "capability.remediation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "capability.gap_analysis", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "deadletter.replay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "supervisor.intervention", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static OperatorExplainabilityItem MapItem(AgentEvent evt)
    {
        var category = ReadMetadata(evt, "category");
        var kind = ResolveKind(evt, category);
        var reasonCode = ReadMetadata(evt, "reason_code");
        var explanation = BuildExplanation(evt, kind, reasonCode);

        return new OperatorExplainabilityItem(
            TimestampUtc: evt.Timestamp,
            AgentId: evt.AgentId,
            Kind: kind,
            EventType: evt.Type.ToString(),
            ReasonCode: reasonCode,
            Explanation: explanation,
            Lineage: new OperatorEventLineage(
                TaskId: ReadMetadata(evt, "task_id"),
                CorrelationId: ReadMetadata(evt, "correlation_id"),
                CausationId: ReadMetadata(evt, "causation_id"),
                DeadLetterId: ReadMetadata(evt, "deadletter_id"),
                RootAgentId: ReadMetadata(evt, "root_agent_id"),
                TargetAgentId: ReadMetadata(evt, "target_agent_id")),
            Metadata: evt.Metadata);
    }

    private static string ResolveKind(AgentEvent evt, string category)
    {
        if (string.Equals(category, "deadletter.replay", StringComparison.OrdinalIgnoreCase))
        {
            return "deadletter_replay";
        }

        if (string.Equals(category, "supervisor.intervention", StringComparison.OrdinalIgnoreCase)
            && string.Equals(ReadMetadata(evt, "supervisor_action"), "preempt", StringComparison.OrdinalIgnoreCase))
        {
            return "branch_preempted";
        }

        if (string.Equals(category, "capability.remediation", StringComparison.OrdinalIgnoreCase)
            && string.Equals(ReadMetadata(evt, "action_kind"), "SwitchExecutionProfile", StringComparison.OrdinalIgnoreCase))
        {
            return "execution_profile_switch";
        }

        if (string.Equals(category, "capability.remediation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "capability.gap_analysis", StringComparison.OrdinalIgnoreCase))
        {
            return "tool_or_skill_creation";
        }

        return "general";
    }

    private static string BuildExplanation(AgentEvent evt, string kind, string reasonCode)
    {
        var category = ReadMetadata(evt, "category");
        return kind switch
        {
            "tool_or_skill_creation" =>
                $"Capability remediation triggered ({category}) with reason '{reasonCode}'. Action={ReadMetadata(evt, "action_kind")} Capability={ReadMetadata(evt, "capability")}.",
            "deadletter_replay" =>
                $"Dead-letter replay {ReadMetadata(evt, "status")} for operation '{ReadMetadata(evt, "operation_name")}' with reason '{reasonCode}'.",
            "execution_profile_switch" =>
                $"Execution profile switch recommended to '{ReadMetadata(evt, "target_execution_profile")}' due to reason '{reasonCode}'.",
            "branch_preempted" =>
                $"Supervisor preempted branch target '{ReadMetadata(evt, "target_agent_id")}' with reason '{reasonCode}'.",
            _ => evt.Description
        };
    }

    private static string ReadMetadata(AgentEvent evt, string key)
    {
        if (!evt.Metadata.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }
}

internal sealed record OperatorExplainabilityReport(
    IReadOnlyList<OperatorExplainabilityItem> Items,
    IReadOnlyDictionary<string, int> Summary);

internal sealed record OperatorExplainabilityItem(
    DateTime TimestampUtc,
    string AgentId,
    string Kind,
    string EventType,
    string ReasonCode,
    string Explanation,
    OperatorEventLineage Lineage,
    Dictionary<string, object> Metadata);

internal sealed record OperatorEventLineage(
    string TaskId,
    string CorrelationId,
    string CausationId,
    string DeadLetterId,
    string RootAgentId,
    string TargetAgentId);