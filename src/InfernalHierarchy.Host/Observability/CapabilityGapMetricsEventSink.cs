using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.Host.Observability;

/// <summary>
/// Decorates the event sink to derive capability-gap counters and latency metrics.
/// </summary>
internal sealed class CapabilityGapMetricsEventSink : IAgentEventSink
{
    private readonly EventStore _inner;
    private readonly MetricsCollector _metrics;

    public CapabilityGapMetricsEventSink(EventStore inner, MetricsCollector metrics)
    {
        _inner = inner;
        _metrics = metrics;
    }

    public void AppendEvent(AgentEvent evt)
    {
        _inner.AppendEvent(evt);
        TryTrackCapabilityGapMetrics(evt);
    }

    private void TryTrackCapabilityGapMetrics(AgentEvent evt)
    {
        if (evt.Type != EventType.DecisionMade)
        {
            return;
        }

        if (!evt.Metadata.TryGetValue("category", out var categoryObj))
        {
            return;
        }

        var category = categoryObj?.ToString() ?? string.Empty;

        if (string.Equals(category, "capability.remediation", StringComparison.OrdinalIgnoreCase)
            && IsGuardrailTriggered(evt.Metadata))
        {
            _metrics.IncrementCounter("guardrail_triggered_total");
            if (evt.Metadata.TryGetValue("note", out var noteObj) && !string.IsNullOrWhiteSpace(noteObj?.ToString()))
            {
                var reason = noteObj!.ToString()!.Trim().ToLowerInvariant().Replace(' ', '_');
                _metrics.IncrementCounter($"guardrail_triggered.{reason}");
            }
        }

        if (string.Equals(category, "capability.replay", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.IncrementCounter("autonomy.replay.total");
            if (evt.Metadata.TryGetValue("status", out var replayStatusObj)
                && string.Equals(replayStatusObj?.ToString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                _metrics.IncrementCounter("autonomy.replay.success");
            }

            UpdateAutonomyRatios();
            return;
        }

        if (!string.Equals(category, "capability.gap_analysis", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(category, "capability.security", StringComparison.OrdinalIgnoreCase)
                && evt.Metadata.TryGetValue("status", out var statusObj)
                && string.Equals(statusObj?.ToString(), "blocked", StringComparison.OrdinalIgnoreCase))
            {
                _metrics.IncrementCounter("autonomy.task.out_of_scope");
                UpdateAutonomyRatios();
            }

            return;
        }

        _metrics.IncrementCounter("capability_gap_detected_total");
        _metrics.IncrementCounter("autonomy.task.total");

        var isOutOfScope = false;

        if (TryGetBool(evt.Metadata, "remediation_attempted", out var attempted) && attempted)
        {
            _metrics.IncrementCounter("capability_gap_autofix_attempt_total");

            if (TryGetDouble(evt.Metadata, "remediation_duration_ms", out var durationMs) && durationMs >= 0)
            {
                _metrics.RecordValue("capability_gap_autofix_duration_ms", durationMs);
            }
        }

        if (TryGetBool(evt.Metadata, "autofix_success", out var success) && success)
        {
            _metrics.IncrementCounter("capability_gap_autofix_success_total");
        }

        if (evt.Metadata.TryGetValue("workflow_state", out var workflowStateObj))
        {
            var workflowState = workflowStateObj?.ToString() ?? string.Empty;
            if (string.Equals(workflowState, "capability_gap_resolved_retrying_original_intent", StringComparison.OrdinalIgnoreCase))
            {
                _metrics.IncrementCounter("autonomy.task.completed");
                _metrics.IncrementCounter("autonomy.task.in_scope_total");
                _metrics.IncrementCounter("autonomy.task.in_scope_completed");
            }
            else if (string.Equals(workflowState, "capability_gap_unresolved_terminal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(workflowState, "capability_gap_policy_blocked", StringComparison.OrdinalIgnoreCase))
            {
                _metrics.IncrementCounter("autonomy.task.terminal_failure");
                if (string.Equals(workflowState, "capability_gap_policy_blocked", StringComparison.OrdinalIgnoreCase))
                {
                    _metrics.IncrementCounter("autonomy.task.out_of_scope");
                    isOutOfScope = true;
                }
                else
                {
                    _metrics.IncrementCounter("autonomy.task.in_scope_total");
                    _metrics.IncrementCounter("autonomy.task.in_scope_terminal_failure");
                }
            }
        }

        if (!isOutOfScope
            && !evt.Metadata.TryGetValue("workflow_state", out _))
        {
            _metrics.IncrementCounter("autonomy.task.in_scope_total");
        }

        if (TryGetDouble(evt.Metadata, "remediation_duration_ms", out var terminalMs) && terminalMs >= 0)
        {
            _metrics.RecordValue("autonomy.time_to_terminal_ms", terminalMs);
        }

        UpdateAutonomyRatios();
    }

    private void UpdateAutonomyRatios()
    {
        var total = _metrics.GetCounter("autonomy.task.total");
        var completed = _metrics.GetCounter("autonomy.task.completed");
        var failed = _metrics.GetCounter("autonomy.task.terminal_failure");
        var outOfScope = _metrics.GetCounter("autonomy.task.out_of_scope");
        var inScopeTotal = _metrics.GetCounter("autonomy.task.in_scope_total");
        var inScopeCompleted = _metrics.GetCounter("autonomy.task.in_scope_completed");
        var inScopeFailed = _metrics.GetCounter("autonomy.task.in_scope_terminal_failure");

        var replayTotal = _metrics.GetCounter("autonomy.replay.total");
        var replaySuccess = _metrics.GetCounter("autonomy.replay.success");

        _metrics.SetGauge("autonomy_task_completion_ratio", total == 0 ? 0 : (double)completed / total);
        _metrics.SetGauge("autonomy_terminal_failure_ratio", total == 0 ? 0 : (double)failed / total);
        _metrics.SetGauge("autonomy_out_of_scope_ratio", total == 0 ? 0 : (double)outOfScope / total);
        _metrics.SetGauge("autonomy_in_scope_task_completion_ratio", inScopeTotal == 0 ? 0 : (double)inScopeCompleted / inScopeTotal);
        _metrics.SetGauge("autonomy_in_scope_terminal_failure_ratio", inScopeTotal == 0 ? 0 : (double)inScopeFailed / inScopeTotal);
        _metrics.SetGauge("autonomy_replay_success_ratio", replayTotal == 0 ? 0 : (double)replaySuccess / replayTotal);
    }

    private static bool IsGuardrailTriggered(IDictionary<string, object> metadata)
    {
        if (!metadata.TryGetValue("status", out var raw) || raw is null)
        {
            return false;
        }

        return string.Equals(raw.ToString(), "guardrail_triggered", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetBool(IDictionary<string, object> metadata, string key, out bool value)
    {
        value = false;

        if (!metadata.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case string s when bool.TryParse(s, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetDouble(IDictionary<string, object> metadata, string key, out double value)
    {
        value = 0;

        if (!metadata.TryGetValue(key, out var raw) || raw is null)
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
            case long l:
                value = l;
                return true;
            case int i:
                value = i;
                return true;
            case string s when double.TryParse(s, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }
}
