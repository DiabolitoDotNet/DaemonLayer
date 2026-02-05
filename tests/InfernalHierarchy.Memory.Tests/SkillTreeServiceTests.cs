using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public sealed class SkillTreeServiceTests
{
    [Fact]
    public async Task GetSkillTreeAsync_ShouldCreateNew_WhenNoFactExists_AndCacheResult()
    {
        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory
            .Setup(m => m.GetFactAsync("skill_tree_a1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Fact?)null);

        var svc = new SkillTreeService(Mock.Of<ILogger<SkillTreeService>>(), sharedMemory.Object);

        var first = await svc.GetSkillTreeAsync("a1");
        var second = await svc.GetSkillTreeAsync("a1");

        first.AgentId.Should().Be("a1");
        second.Should().BeSameAs(first);

        sharedMemory.Verify(m => m.GetFactAsync("skill_tree_a1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AwardExperienceAsync_ShouldPersistAsNewFact_WhenNoneExists()
    {
        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory
            .Setup(m => m.GetFactAsync("skill_tree_a1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Fact?)null);
        sharedMemory
            .Setup(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = new SkillTreeService(Mock.Of<ILogger<SkillTreeService>>(), sharedMemory.Object);

        var result = await svc.AwardExperienceAsync(
            agentId: "a1",
            toolName: "web_search",
            success: true,
            executionTime: TimeSpan.FromMilliseconds(10),
            complexity: 1);

        result.Should().NotBeNull();
        sharedMemory.Verify(m => m.AddFactAsync(It.Is<Fact>(f => f.Id == "skill_tree_a1" && f.Category == "skill_tree"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAgentsByMasteryAsync_ShouldReturnQualifiedAgents_AndSkipBadJson()
    {
        var goodTree = new AgentSkillTree { AgentId = "a1" };
        goodTree.GetOrCreateSkill("web_search").MasteryLevel = MasteryLevel.Expert;

        var facts = new List<Fact>
        {
            new Fact { Id = "f1", Content = JsonSerializer.Serialize(goodTree) },
            new Fact { Id = "f2", Content = "{not-json" }
        };

        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory
            .Setup(m => m.GetFactsByCategoryAsync("skill_tree", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var svc = new SkillTreeService(Mock.Of<ILogger<SkillTreeService>>(), sharedMemory.Object);

        var agents = await svc.GetAgentsByMasteryAsync("web_search", MasteryLevel.Expert);

        agents.Should().ContainSingle().Which.Should().Be("a1");
    }

    [Theory]
    [InlineData(AgentRank.Supreme, "create_sub_agent")]
    [InlineData(AgentRank.Prince, "create_sub_agent")]
    [InlineData(AgentRank.Duke, "web_search")]
    [InlineData(AgentRank.Worker, "read_memory")]
    public async Task GetRecommendedSkillsAsync_ShouldReturnRankBasedRecommendations(AgentRank rank, string expected)
    {
        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory
            .Setup(m => m.GetFactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Fact?)null);

        var svc = new SkillTreeService(Mock.Of<ILogger<SkillTreeService>>(), sharedMemory.Object);

        var skills = await svc.GetRecommendedSkillsAsync("a1", rank);

        skills.Should().Contain(expected);
    }

    [Fact]
    public async Task GetEfficiencyMultiplierAsync_ShouldReturnFromSkillTree()
    {
        var tree = new AgentSkillTree { AgentId = "a1" };
        tree.GetOrCreateSkill("web_search").MasteryLevel = MasteryLevel.Expert;

        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory
            .Setup(m => m.GetFactAsync("skill_tree_a1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fact { Id = "skill_tree_a1", Content = JsonSerializer.Serialize(tree) });

        var svc = new SkillTreeService(Mock.Of<ILogger<SkillTreeService>>(), sharedMemory.Object);

        var multiplier = await svc.GetEfficiencyMultiplierAsync("a1", "web_search");

        multiplier.Should().Be(1.3);
    }

    [Fact]
    public async Task GetSuccessRateBonusAsync_ShouldReturnFromSkillTree()
    {
        var tree = new AgentSkillTree { AgentId = "a1" };
        tree.GetOrCreateSkill("web_search").MasteryLevel = MasteryLevel.Proficient;

        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory
            .Setup(m => m.GetFactAsync("skill_tree_a1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fact { Id = "skill_tree_a1", Content = JsonSerializer.Serialize(tree) });

        var svc = new SkillTreeService(Mock.Of<ILogger<SkillTreeService>>(), sharedMemory.Object);

        var bonus = await svc.GetSuccessRateBonusAsync("a1", "web_search");

        bonus.Should().Be(0.15);
    }

    [Fact]
    public async Task GetStatsAsync_ShouldAggregateSkillTreeFacts_AndSkipBadJson()
    {
        var t1 = new AgentSkillTree { AgentId = "a1", TotalExperiencePoints = 100 };
        t1.GetOrCreateSkill("web_search").Level = 4;
        t1.GetOrCreateSkill("web_search").MasteryLevel = MasteryLevel.Competent;
        t1.GetOrCreateSkill("read_memory").Level = 8;
        t1.GetOrCreateSkill("read_memory").MasteryLevel = MasteryLevel.Expert;

        var t2 = new AgentSkillTree { AgentId = "a2", TotalExperiencePoints = 50 };
        t2.GetOrCreateSkill("web_search").Level = 2;
        t2.GetOrCreateSkill("web_search").MasteryLevel = MasteryLevel.Apprentice;

        var facts = new List<Fact>
        {
            new() { Id = "f1", Category = "skill_tree", Content = JsonSerializer.Serialize(t1) },
            new() { Id = "f2", Category = "skill_tree", Content = JsonSerializer.Serialize(t2) },
            new() { Id = "bad", Category = "skill_tree", Content = "{not-json" }
        };

        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory
            .Setup(m => m.GetFactsByCategoryAsync("skill_tree", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var svc = new SkillTreeService(Mock.Of<ILogger<SkillTreeService>>(), sharedMemory.Object);

        var stats = await svc.GetStatsAsync();

        stats.TotalAgentsWithSkills.Should().Be(2);
        stats.TotalSkillsTracked.Should().Be(3);
        stats.MostCommonSkills.Select(s => s.ToolName).Should().Contain("web_search");
        stats.TopAgentsByExperience.Select(a => a.AgentId).Should().Contain(new[] { "a1", "a2" });
        stats.MasteryDistribution.Should().ContainKey(MasteryLevel.Expert);
    }
}
