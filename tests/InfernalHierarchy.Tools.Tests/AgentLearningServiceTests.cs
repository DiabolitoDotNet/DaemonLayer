using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class AgentLearningServiceTests
{
    private static readonly string[] WebSearchAndReadMemoryTools = new[] { "web_search", "read_memory" };
    private static readonly string[] TwoAvailableTools = new[] { "a", "b" };

    [Fact]
    public async Task RecordToolExecutionAsync_ShouldNotThrow_WhenSkillTreeServiceThrows()
    {
        var skillTree = new Mock<ISkillTreeService>();
        skillTree
            .Setup(s => s.AwardExperienceAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var svc = new AgentLearningService(Mock.Of<ILogger<AgentLearningService>>(), skillTree.Object);

        var act = async () => await svc.RecordToolExecutionAsync(
            agentId: "a1",
            agentRank: AgentRank.Duke.ToString(),
            toolName: "web_search",
            success: true,
            duration: TimeSpan.FromMilliseconds(10),
            complexity: 2);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAgentStats_ShouldReturnTopToolsAndOverallRate()
    {
        var svc = new AgentLearningService(Mock.Of<ILogger<AgentLearningService>>(), skillTreeService: null);

        await svc.RecordToolExecutionAsync("a1", "Duke", "t1", success: true, TimeSpan.FromMilliseconds(5));
        await svc.RecordToolExecutionAsync("a1", "Duke", "t1", success: true, TimeSpan.FromMilliseconds(5));
        await svc.RecordToolExecutionAsync("a1", "Duke", "t2", success: false, TimeSpan.FromMilliseconds(5));

        var stats = svc.GetAgentStats("a1");

        stats.Should().NotBeNull();
        stats!.AgentId.Should().Be("a1");
        stats.TotalToolExecutions.Should().Be(3);
        stats.TopTools.Should().NotBeEmpty();
        stats.OverallSuccessRate.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetSystemStats_ShouldCountGlobalTools_ByAgentIdNull()
    {
        var svc = new AgentLearningService(Mock.Of<ILogger<AgentLearningService>>(), skillTreeService: null);

        await svc.RecordToolExecutionAsync("a1", "Duke", "web_search", success: true, TimeSpan.FromMilliseconds(5));
        await svc.RecordToolExecutionAsync("a2", "Worker", "web_search", success: false, TimeSpan.FromMilliseconds(5));
        await svc.RecordToolExecutionAsync("a2", "Worker", "read_memory", success: true, TimeSpan.FromMilliseconds(5));

        var system = svc.GetSystemStats();

        system.TotalAgentsTracked.Should().Be(2);
        system.TotalToolsTracked.Should().Be(2);
        system.GlobalToolStats.Select(x => x.ToolName).Should().Contain(WebSearchAndReadMemoryTools);
    }

    [Fact]
    public void GetRecommendedTools_ShouldReturnAvailableTools_WhenNoProfileExists()
    {
        var svc = new AgentLearningService(Mock.Of<ILogger<AgentLearningService>>(), skillTreeService: null);

        var tools = svc.GetRecommendedTools("missing", TwoAvailableTools);

        tools.Should().Equal("a", "b");
    }
}
