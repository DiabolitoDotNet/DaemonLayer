using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Host.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class CapabilityGapMetricsEventSinkTests
{
    [Fact]
    public void AppendEvent_ShouldDeriveAutonomySloMetrics_FromGapAndReplayEvents()
    {
        var metrics = new MetricsCollector();
        var storePath = Path.Combine(Path.GetTempPath(), $"host_events_{Guid.NewGuid():N}");
        var eventStore = new EventStore(storePath, NullLogger<EventStore>.Instance);
        var sut = new CapabilityGapMetricsEventSink(eventStore, metrics);

        sut.AppendEvent(new AgentEvent
        {
            AgentId = "lucifer",
            Type = EventType.DecisionMade,
            Description = "Gap analysis complete",
            Metadata = new Dictionary<string, object>
            {
                ["category"] = "capability.gap_analysis",
                ["workflow_state"] = "capability_gap_resolved_retrying_original_intent",
                ["remediation_attempted"] = true,
                ["autofix_success"] = true,
                ["remediation_duration_ms"] = 120d
            }
        });

        sut.AppendEvent(new AgentEvent
        {
            AgentId = "lucifer",
            Type = EventType.DecisionMade,
            Description = "Replay outcome",
            Metadata = new Dictionary<string, object>
            {
                ["category"] = "capability.replay",
                ["status"] = "success",
                ["attempts"] = 1
            }
        });

        sut.AppendEvent(new AgentEvent
        {
            AgentId = "lucifer",
            Type = EventType.DecisionMade,
            Description = "Gap analysis blocked",
            Metadata = new Dictionary<string, object>
            {
                ["category"] = "capability.gap_analysis",
                ["workflow_state"] = "capability_gap_policy_blocked",
                ["remediation_attempted"] = false,
                ["autofix_success"] = false,
                ["remediation_duration_ms"] = 240d
            }
        });

        metrics.GetCounter("autonomy.task.total").Should().Be(2);
        metrics.GetCounter("autonomy.task.completed").Should().Be(1);
        metrics.GetCounter("autonomy.task.terminal_failure").Should().Be(1);
        metrics.GetCounter("autonomy.task.out_of_scope").Should().Be(1);
        metrics.GetCounter("autonomy.task.in_scope_total").Should().Be(1);
        metrics.GetCounter("autonomy.task.in_scope_completed").Should().Be(1);
        metrics.GetCounter("autonomy.task.in_scope_terminal_failure").Should().Be(0);
        metrics.GetCounter("autonomy.replay.total").Should().Be(1);
        metrics.GetCounter("autonomy.replay.success").Should().Be(1);

        metrics.GetGauge("autonomy_task_completion_ratio").Should().BeApproximately(0.5, 0.0001);
        metrics.GetGauge("autonomy_terminal_failure_ratio").Should().BeApproximately(0.5, 0.0001);
        metrics.GetGauge("autonomy_out_of_scope_ratio").Should().BeApproximately(0.5, 0.0001);
        metrics.GetGauge("autonomy_in_scope_task_completion_ratio").Should().BeApproximately(1.0, 0.0001);
        metrics.GetGauge("autonomy_in_scope_terminal_failure_ratio").Should().BeApproximately(0.0, 0.0001);
        metrics.GetGauge("autonomy_replay_success_ratio").Should().BeApproximately(1.0, 0.0001);

        var terminalStats = metrics.GetHistogramStats("autonomy.time_to_terminal_ms");
        terminalStats.Count.Should().Be(2);
        terminalStats.P50.Should().Be(120d);
        terminalStats.P95.Should().Be(240d);
    }
}