using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Host;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class ResourceLimitServiceAdditionalTests
{
    [Fact]
    public void GetStatus_ReflectsLimitsAndCurrentSlots()
    {
        var limits = new ResourceLimits
        {
            MaxTotalAgents = 10,
            MaxSupremeAgents = 1,
            MaxPrinceAgents = 2,
            MaxDukeAgents = 3,
            MaxWorkerAgents = 4,
            MaxConcurrentToolExecutions = 2,
            MaxDatabaseSizeBytes = 50L * 1024 * 1024,
        };

        var service = new ResourceLimitService(limits);

        var status = service.GetStatus();

        status.MaxTotalAgents.Should().Be(10);
        status.MaxSupremeAgents.Should().Be(1);
        status.MaxPrinceAgents.Should().Be(2);
        status.MaxDukeAgents.Should().Be(3);
        status.MaxWorkerAgents.Should().Be(4);
        status.MaxConcurrentToolExecutions.Should().Be(2);
        status.AvailableToolExecutionSlots.Should().Be(2);
        status.MaxDatabaseSizeMB.Should().Be(50);
    }

    [Theory]
    [InlineData(AgentRank.Supreme, 0, 0, true)]
    [InlineData(AgentRank.Supreme, 1, 0, false)]
    [InlineData(AgentRank.Prince, 1, 0, true)]
    [InlineData(AgentRank.Prince, 2, 0, false)]
    public void CanCreateAgent_EnforcesRankAndTotalLimits(AgentRank rank, int currentCount, int totalAgents, bool expected)
    {
        var limits = new ResourceLimits
        {
            MaxTotalAgents = 10,
            MaxSupremeAgents = 1,
            MaxPrinceAgents = 2,
            MaxDukeAgents = 3,
            MaxWorkerAgents = 4,
        };

        var service = new ResourceLimitService(limits);

        service.CanCreateAgent(rank, currentCount, totalAgents).Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteToolWithLimitAsync_FuncTask_PropagatesResultAsync()
    {
        var limits = new ResourceLimits { MaxConcurrentToolExecutions = 1, MaxToolExecutionTimeSeconds = 10 };
        var service = new ResourceLimitService(limits);

        var result = await service.ExecuteToolWithLimitAsync(() => Task.FromResult(123));

        result.Should().Be(123);
    }

    [Fact]
    public async Task ExecuteToolWithLimitAsync_FuncWithToken_PropagatesResultAsync()
    {
        var limits = new ResourceLimits { MaxConcurrentToolExecutions = 1, MaxToolExecutionTimeSeconds = 10 };
        var service = new ResourceLimitService(limits);

        var result = await service.ExecuteToolWithLimitAsync(ct => Task.FromResult(ct.IsCancellationRequested ? 0 : 456));

        result.Should().Be(456);
    }
}
