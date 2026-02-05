using FluentAssertions;
using InfernalHierarchy.Core;
using InfernalHierarchy.Core.Entities;
using Xunit;

namespace InfernalHierarchy.Core.Tests;

public sealed class MemoryVisibilityRulesTests
{
    private sealed class TestEntry : MemoryEntry
    {
    }

    [Fact]
    public void CanView_ShouldReturnTrue_WhenRequesterIsCreator()
    {
        var entry = new TestEntry
        {
            CreatedBy = "a1",
            Visibility = MemoryVisibility.Private
        };

        MemoryVisibilityRules.CanView(entry, requestingAgentId: "a1", requestingAgentRank: AgentRank.Worker)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(MemoryVisibility.Public, true)]
    [InlineData(MemoryVisibility.Private, false)]
    public void CanView_PublicPrivateBehaviors(MemoryVisibility visibility, bool expected)
    {
        var entry = new TestEntry
        {
            CreatedBy = "owner",
            Visibility = visibility
        };

        MemoryVisibilityRules.CanView(entry, requestingAgentId: "other", requestingAgentRank: AgentRank.Worker)
            .Should().Be(expected);
    }

    [Fact]
    public void CanView_Shared_ShouldRequireAgentInList()
    {
        var entry = new TestEntry
        {
            CreatedBy = "owner",
            Visibility = MemoryVisibility.Shared,
            SharedWithAgents = new() { "a2" }
        };

        MemoryVisibilityRules.CanView(entry, requestingAgentId: "a2", requestingAgentRank: AgentRank.Worker)
            .Should().BeTrue();
        MemoryVisibilityRules.CanView(entry, requestingAgentId: "a3", requestingAgentRank: AgentRank.Worker)
            .Should().BeFalse();
    }

    [Fact]
    public void CanView_RankBased_ShouldRequireMinimumRank_AndAllowHigherRank()
    {
        var entry = new TestEntry
        {
            CreatedBy = "owner",
            Visibility = MemoryVisibility.RankBased,
            MinimumRankToView = AgentRank.Duke
        };

        MemoryVisibilityRules.CanView(entry, requestingAgentId: "w", requestingAgentRank: AgentRank.Worker)
            .Should().BeFalse();
        MemoryVisibilityRules.CanView(entry, requestingAgentId: "p", requestingAgentRank: AgentRank.Prince)
            .Should().BeTrue();
    }

    [Fact]
    public void CanView_RankBased_ShouldReturnFalse_WhenMinimumRankMissing()
    {
        var entry = new TestEntry
        {
            CreatedBy = "owner",
            Visibility = MemoryVisibility.RankBased,
            MinimumRankToView = null
        };

        MemoryVisibilityRules.CanView(entry, requestingAgentId: "p", requestingAgentRank: AgentRank.Prince)
            .Should().BeFalse();
    }

    [Fact]
    public void CanView_ShouldReturnFalse_ForUnknownVisibility()
    {
        var entry = new TestEntry
        {
            CreatedBy = "owner",
            Visibility = (MemoryVisibility)999
        };

        MemoryVisibilityRules.CanView(entry, requestingAgentId: "other", requestingAgentRank: AgentRank.Supreme)
            .Should().BeFalse();
    }
}
