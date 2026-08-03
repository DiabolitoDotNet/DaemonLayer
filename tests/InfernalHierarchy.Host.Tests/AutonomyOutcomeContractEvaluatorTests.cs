using FluentAssertions;
using InfernalHierarchy.Host.Api;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AutonomyOutcomeContractEvaluatorTests
{
    [Fact]
    public void EnrichAutonomyOutcomePayload_WhenTerminalHasNoFallbackSignals_ShouldMarkAutonomousSuccess()
    {
        var payload = new Dictionary<string, object>();

        var enriched = AutonomyOutcomeContractEvaluator.EnrichAutonomyOutcomePayload("Task completed successfully.", payload);

        enriched["autonomy_outcome_status"].Should().Be("success");
        enriched["autonomy_outcome_autonomous_success"].Should().Be(true);
        enriched["autonomy_outcome_needs_supervisor_intervention"].Should().Be(false);
        enriched["autonomy_outcome_next_action"].Should().Be("none");
    }

    [Fact]
    public void EnrichAutonomyOutcomePayload_WhenNextActionIsNotNone_ShouldMarkNonAutonomousTerminal()
    {
        var payload = new Dictionary<string, object>
        {
            ["next_action"] = "fallback_to_local_collaboration"
        };

        var enriched = AutonomyOutcomeContractEvaluator.EnrichAutonomyOutcomePayload("Decision produced.", payload);

        enriched["autonomy_outcome_status"].Should().Be("non_autonomous_terminal");
        enriched["autonomy_outcome_autonomous_success"].Should().Be(false);
        enriched["autonomy_outcome_next_action"].Should().Be("fallback_to_local_collaboration");
    }

    [Fact]
    public void EnrichAutonomyOutcomePayload_WhenCapabilityGapBlocked_ShouldMarkAutonomyBlocked()
    {
        var payload = new Dictionary<string, object>
        {
            ["capability_gap_state"] = "blocked_by_sensitive_input_guard",
            ["capability_gap_terminal_reason_code"] = "secret_reference_required"
        };

        var enriched = AutonomyOutcomeContractEvaluator.EnrichAutonomyOutcomePayload("Blocked by policy.", payload);

        enriched["autonomy_outcome_status"].Should().Be("autonomy_blocked");
        enriched["autonomy_outcome_reason_code"].Should().Be("secret_reference_required");
        enriched["autonomy_outcome_autonomous_success"].Should().Be(false);
    }

    [Fact]
    public void BuildTimeoutOutcomePayload_ShouldEmitDeterministicTimeoutContract()
    {
        var timeoutPayload = AutonomyOutcomeContractEvaluator.BuildTimeoutOutcomePayload();

        timeoutPayload["autonomy_outcome_status"].Should().Be("timeout");
        timeoutPayload["autonomy_outcome_reason_code"].Should().Be("playground_timeout");
        timeoutPayload["autonomy_outcome_autonomous_success"].Should().Be(false);
        timeoutPayload["autonomy_outcome_next_action"].Should().Be("none");
        timeoutPayload["autonomy_outcome_needs_supervisor_intervention"].Should().Be(false);
    }
}
