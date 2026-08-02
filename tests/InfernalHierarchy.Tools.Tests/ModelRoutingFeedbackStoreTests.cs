using FluentAssertions;
using InfernalHierarchy.Tools.Clients;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class ModelRoutingFeedbackStoreTests
{
    [Fact]
    public void GetPenalty_WhenNoData_ReturnsZero()
    {
        var store = new InMemoryModelRoutingFeedbackStore();

        var penalty = store.GetPenalty("unknown-model");

        penalty.Should().Be(0);
    }

    [Fact]
    public void RecordOutcome_ShouldIncreasePenaltyForFailuresAndLatency()
    {
        var store = new InMemoryModelRoutingFeedbackStore();

        for (var i = 0; i < 10; i++)
        {
            store.RecordOutcome("reliable", success: true, TimeSpan.FromMilliseconds(500), outputTokens: 100);
            store.RecordOutcome("fragile", success: false, TimeSpan.FromMilliseconds(2500), outputTokens: 0);
        }

        var reliablePenalty = store.GetPenalty("reliable");
        var fragilePenalty = store.GetPenalty("fragile");

        fragilePenalty.Should().BeGreaterThan(reliablePenalty);

        var snapshots = store.GetSnapshots();
        snapshots.Should().ContainKey("reliable");
        snapshots.Should().ContainKey("fragile");
        snapshots["fragile"].FailureRate.Should().BeGreaterThan(0);
        snapshots["fragile"].AvgLatencyMs.Should().BeGreaterThan(snapshots["reliable"].AvgLatencyMs);
    }
}
