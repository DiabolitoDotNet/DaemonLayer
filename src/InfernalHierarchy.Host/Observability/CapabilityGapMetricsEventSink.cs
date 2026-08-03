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

        if (!string.Equals(category, "capability.gap_analysis", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _metrics.IncrementCounter("capability_gap_detected_total");

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
