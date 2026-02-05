using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class ListTemplatesToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldListAllTemplates_WhenNoCategoryProvided()
    {
        var templates = new[]
        {
            new AgentTemplate
            {
                TemplateId = "t1",
                Name = "Alpha",
                Category = TemplateCategory.DataAnalysis,
                Description = "D1",
                RecommendedRank = AgentRank.Worker,
                DefaultTools = new List<string> { "read_memory" },
                Tags = new List<string> { "tag" },
                UsageCount = 2
            },
            new AgentTemplate
            {
                TemplateId = "t2",
                Name = "Beta",
                Category = TemplateCategory.Research,
                Description = "D2",
                RecommendedRank = AgentRank.Duke,
                DefaultTools = new List<string> { "web_search" },
                Tags = new List<string> { "tag2" },
                UsageCount = 1
            }
        };

        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.GetAllTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var tool = new ListTemplatesTool(
            Mock.Of<ILogger<ListTemplatesTool>>(),
            templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Available Agent Templates");
        result.Output.Should().Contain("Alpha");
        result.Output.Should().Contain("Beta");
        result.Output.Should().Contain("Total Templates");
        result.Output.Should().Contain("2");

        templateService.Verify(s => s.GetAllTemplatesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFilterByCategory_WhenCategoryProvided()
    {
        var templates = new[]
        {
            new AgentTemplate
            {
                TemplateId = "t1",
                Name = "Alpha",
                Category = TemplateCategory.DataAnalysis,
                Description = "D1",
                RecommendedRank = AgentRank.Worker,
                DefaultTools = new List<string> { "read_memory" },
                Tags = new List<string> { "tag" },
                UsageCount = 2
            }
        };

        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.GetTemplatesByCategoryAsync(TemplateCategory.DataAnalysis, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var tool = new ListTemplatesTool(
            Mock.Of<ILogger<ListTemplatesTool>>(),
            templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["category"] = "DataAnalysis" });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Alpha");

        templateService.Verify(s => s.GetTemplatesByCategoryAsync(TemplateCategory.DataAnalysis, It.IsAny<CancellationToken>()), Times.Once);
        templateService.Verify(s => s.GetAllTemplatesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallbackToAllTemplates_WhenCategoryInvalid()
    {
        var templates = new[]
        {
            new AgentTemplate
            {
                TemplateId = "t1",
                Name = "Alpha",
                Category = TemplateCategory.DataAnalysis,
                Description = "D1",
                RecommendedRank = AgentRank.Worker,
                DefaultTools = new List<string> { "read_memory" },
                Tags = new List<string> { "tag" },
                UsageCount = 2
            }
        };

        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.GetAllTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var tool = new ListTemplatesTool(
            Mock.Of<ILogger<ListTemplatesTool>>(),
            templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["category"] = "not-a-category" });

        result.Success.Should().BeTrue();
        templateService.Verify(s => s.GetAllTemplatesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldListAllTemplates_WhenCategoryNotString()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.GetAllTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AgentTemplate>());

        var tool = new ListTemplatesTool(
            Mock.Of<ILogger<ListTemplatesTool>>(),
            templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["category"] = 123 });

        result.Success.Should().BeTrue();
        templateService.Verify(s => s.GetAllTemplatesAsync(It.IsAny<CancellationToken>()), Times.Once);
        templateService.Verify(s => s.GetTemplatesByCategoryAsync(It.IsAny<TemplateCategory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenTemplateServiceThrows()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.GetAllTemplatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var tool = new ListTemplatesTool(
            Mock.Of<ILogger<ListTemplatesTool>>(),
            templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Failed to list templates");
    }
}
