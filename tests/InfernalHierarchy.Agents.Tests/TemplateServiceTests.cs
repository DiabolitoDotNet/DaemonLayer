using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class TemplateServiceTests
{
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "InfernalHierarchy.TemplateServiceTests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    private static AgentTemplate CreateTemplate(string id)
        => new()
        {
            TemplateId = id,
            Name = "Data Analyst",
            Category = TemplateCategory.DataAnalysis,
            Description = "Analyzes things",
            RecommendedRank = AgentRank.Duke,
            SystemPromptTemplate = "Hello {agent_name}. Missing={missing}",
            DefaultTools = ["web_search"],
            Tags = ["analysis", "data"],
            MergeParameters = new Dictionary<string, string> { ["domain"] = "finance" }
        };

    [Fact]
    public void Constructor_CreatesTemplatesDirectory_WhenMissing()
    {
        using var tmp = new TempDirectory();
        Directory.Exists(tmp.Path).Should().BeFalse();

        _ = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        Directory.Exists(tmp.Path).Should().BeTrue();
    }

    [Fact]
    public async Task RegisterTemplateAsync_SavesFile_AndCachesTemplate()
    {
        using var tmp = new TempDirectory();
        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var template = CreateTemplate("t1");

        (await sut.RegisterTemplateAsync(template)).Should().BeTrue();

        File.Exists(System.IO.Path.Combine(tmp.Path, "t1.json")).Should().BeTrue();

        var loaded = await sut.GetTemplateAsync("t1");
        loaded.Should().NotBeNull();
        loaded!.TemplateId.Should().Be("t1");

        (await sut.RegisterTemplateAsync(template)).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterTemplateAsync_WithoutTemplateId_ReturnsFalse()
    {
        using var tmp = new TempDirectory();
        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var template = CreateTemplate(string.Empty);

        (await sut.RegisterTemplateAsync(template)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateTemplateAsync_WhenMissing_ReturnsFalse()
    {
        using var tmp = new TempDirectory();
        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var template = CreateTemplate("t1");
        (await sut.UpdateTemplateAsync(template)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateTemplateAsync_WhenExists_PersistsToDisk()
    {
        using var tmp = new TempDirectory();
        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var template = CreateTemplate("t1");
        (await sut.RegisterTemplateAsync(template)).Should().BeTrue();

        template.Description = "Updated";
        (await sut.UpdateTemplateAsync(template)).Should().BeTrue();

        var sut2 = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var loaded = await sut2.GetTemplateAsync("t1");
        loaded!.Description.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteTemplateAsync_RemovesFile_AndCacheEntry()
    {
        using var tmp = new TempDirectory();
        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var template = CreateTemplate("t1");
        (await sut.RegisterTemplateAsync(template)).Should().BeTrue();
        var filePath = System.IO.Path.Combine(tmp.Path, "t1.json");
        File.Exists(filePath).Should().BeTrue();

        (await sut.DeleteTemplateAsync("t1")).Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();

        (await sut.GetTemplateAsync("t1")).Should().BeNull();
    }

    [Fact]
    public async Task SearchTemplatesAsync_RanksExactIdHighest()
    {
        using var tmp = new TempDirectory();
        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var exact = CreateTemplate("exact-id");
        exact.Name = "Something else";
        var other = CreateTemplate("t2");
        other.Name = "exact-id-ish";
        other.Description = "contains exact-id";

        (await sut.RegisterTemplateAsync(exact)).Should().BeTrue();
        (await sut.RegisterTemplateAsync(other)).Should().BeTrue();

        var results = (await sut.SearchTemplatesAsync("exact-id")).ToList();
        results.Should().NotBeEmpty();
        results[0].TemplateId.Should().Be("exact-id");
    }

    [Fact]
    public async Task GetAllTemplatesAsync_LoadsFromDisk_WhenCacheEmpty()
    {
        using var tmp = new TempDirectory();

        var sut1 = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        (await sut1.RegisterTemplateAsync(CreateTemplate("t1"))).Should().BeTrue();
        (await sut1.RegisterTemplateAsync(CreateTemplate("t2"))).Should().BeTrue();

        var sut2 = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var all = (await sut2.GetAllTemplatesAsync()).ToList();
        all.Select(t => t.TemplateId).Should().BeEquivalentTo(["t1", "t2"]);
    }

    [Fact]
    public async Task InstantiateTemplateAsync_WhenTemplateMissing_ReturnsError()
    {
        using var tmp = new TempDirectory();
        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        var result = await sut.InstantiateTemplateAsync("missing", "agent");
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task InstantiateTemplateAsync_WhenAgentFactoryReturnsNull_ReturnsError()
    {
        using var tmp = new TempDirectory();

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory
            .Setup(f => f.CreateAgentAsync(It.IsAny<string>(), It.IsAny<AgentRank>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAgent)null!);

        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            agentFactory.Object,
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);

        (await sut.RegisterTemplateAsync(CreateTemplate("t1"))).Should().BeTrue();

        var result = await sut.InstantiateTemplateAsync("t1", "agent");
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("factory");
    }

    [Fact]
    public async Task InstantiateTemplateAsync_Success_MergesParameters_IncrementsUsage_AndInitializesSkillTree()
    {
        using var tmp = new TempDirectory();

        var agent = new Mock<IAgent>();
        agent.SetupGet(a => a.Id).Returns("agent-123");
        agent.SetupGet(a => a.Name).Returns("agent");
        agent.SetupGet(a => a.Rank).Returns(AgentRank.Duke);
        agent.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        agent.SetupGet(a => a.Persona).Returns(new Persona { Name = "agent" });

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory
            .Setup(f => f.CreateAgentAsync("agent", AgentRank.Duke, "parent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent.Object);

        var skillTree = new Mock<ISkillTreeService>();
        skillTree
            .Setup(s => s.AwardExperienceAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillProgressionResult());

        var sut = new TemplateService(
            NullLogger<TemplateService>.Instance,
            agentFactory.Object,
            skillTree.Object,
            templatesDirectory: tmp.Path);

        var template = CreateTemplate("t1");
        template.SkillTree = new TemplateSkillTree
        {
            InitialSkills = new Dictionary<string, int>
            {
                ["web_search"] = 2,
                ["code_generator"] = 1,
            }
        };

        (await sut.RegisterTemplateAsync(template)).Should().BeTrue();

        var result = await sut.InstantiateTemplateAsync(
            "t1",
            "agent",
            parameters: new Dictionary<string, string> { ["domain"] = "security" },
            parentAgentId: "parent");

        result.Success.Should().BeTrue();
        result.AgentId.Should().Be("agent-123");
        result.AppliedParameters.Should().ContainKey("agent_name").WhoseValue.Should().Be("agent");
        result.AppliedParameters["domain"].Should().Be("security");

        // Skill tree initialization: web_search levels 1..2 (2 calls), code_generator level 1 (1 call)
        skillTree.Verify(s => s.AwardExperienceAsync(
            "agent-123",
            "web_search",
            true,
            It.IsAny<TimeSpan>(),
            1,
            It.IsAny<CancellationToken>()), Times.Once);
        skillTree.Verify(s => s.AwardExperienceAsync(
            "agent-123",
            "web_search",
            true,
            It.IsAny<TimeSpan>(),
            2,
            It.IsAny<CancellationToken>()), Times.Once);
        skillTree.Verify(s => s.AwardExperienceAsync(
            "agent-123",
            "code_generator",
            true,
            It.IsAny<TimeSpan>(),
            1,
            It.IsAny<CancellationToken>()), Times.Once);

        (await sut.GetTemplateAsync("t1"))!.UsageCount.Should().Be(1);

        var sut2 = new TemplateService(
            NullLogger<TemplateService>.Instance,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ISkillTreeService>(),
            templatesDirectory: tmp.Path);
        (await sut2.GetTemplateAsync("t1"))!.UsageCount.Should().Be(1);

        // Sanity check: the file is JSON and readable.
        var json = await File.ReadAllTextAsync(System.IO.Path.Combine(tmp.Path, "t1.json"));
        JsonDocument.Parse(json).RootElement.GetProperty("usageCount").GetInt32().Should().Be(1);
    }
}
