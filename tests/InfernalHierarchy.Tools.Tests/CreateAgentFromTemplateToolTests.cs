using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class CreateAgentFromTemplateToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenTemplateIdMissing()
    {
        var templateService = new Mock<ITemplateService>();
        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["agent_name"] = "A" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("template_id");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenAgentNameMissing()
    {
        var templateService = new Mock<ITemplateService>();
        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["template_id"] = "t" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("agent_name");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldInstantiateTemplateAndReturnSuccess()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                "data-analyst-basic",
                "FinanceAnalyst",
                It.IsAny<Dictionary<string, string>?>(),
                "parent",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateInstantiationResult
            {
                Success = true,
                AgentId = "agent-123",
                AppliedParameters = new Dictionary<string, string> { ["domain"] = "finance" },
                Warnings = new List<string> { "w" }
            });

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "data-analyst-basic",
            ["agent_name"] = "FinanceAnalyst",
            ["parameters"] = "{\"domain\":\"finance\"}",
            ["parent_id"] = "parent"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("agent-123");
        result.Output.Should().Contain("Applied Parameters");
        result.Output.Should().Contain("Warnings");

        templateService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailure_WhenInstantiationFails()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                "t",
                "A",
                It.IsAny<Dictionary<string, string>?>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateInstantiationResult { Success = false, Error = "nope" });

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = "A"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("nope");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIgnoreInvalidParametersJson()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                "t",
                "A",
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateInstantiationResult { Success = true, AgentId = "id" });

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = "A",
            ["parameters"] = "{not-json"
        });

        result.Success.Should().BeTrue();
        templateService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPassParametersDictionary_WhenProvidedAsDictionary()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                "t",
                "A",
                It.Is<Dictionary<string, string>?>(p => p != null && p["k"] == "v"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateInstantiationResult { Success = true, AgentId = "id" });

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = "A",
            ["parameters"] = new Dictionary<string, string> { ["k"] = "v" }
        });

        result.Success.Should().BeTrue();
        templateService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnDefaultFailureMessage_WhenInstantiationFailsWithoutError()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                "t",
                "A",
                It.IsAny<Dictionary<string, string>?>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateInstantiationResult { Success = false, Error = null });

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = "A"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Template instantiation failed");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldOmitParentAndOptionalSections_WhenEmpty()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                "t",
                "A",
                It.IsAny<Dictionary<string, string>?>(),
                " ",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateInstantiationResult
            {
                Success = true,
                AgentId = "id",
                AppliedParameters = new Dictionary<string, string>(),
                Warnings = new List<string>()
            });

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = "A",
            ["parent_id"] = " "
        });

        result.Success.Should().BeTrue();
        result.Output.Should().NotContain("Parent:");
        result.Output.Should().NotContain("Applied Parameters:");
        result.Output.Should().NotContain("Warnings:");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenTemplateServiceThrows()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = "A"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Template instantiation error");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenTemplateIdIsNotString()
    {
        var templateService = new Mock<ITemplateService>();
        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = 123,
            ["agent_name"] = "A"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("template_id");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenAgentNameIsNotString()
    {
        var templateService = new Mock<ITemplateService>();
        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = 456
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("agent_name");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIgnoreParameters_WhenParametersTypeUnsupported()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                "t",
                "A",
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateInstantiationResult { Success = true, AgentId = "id" });

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        using var doc = JsonDocument.Parse("{\"k\":\"v\"}");

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = "A",
            ["parameters"] = doc.RootElement
        });

        result.Success.Should().BeTrue();
        templateService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTreatNonStringParentIdAsNull()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(s => s.InstantiateTemplateAsync(
                "t",
                "A",
                It.IsAny<Dictionary<string, string>?>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateInstantiationResult { Success = true, AgentId = "id" });

        var tool = new CreateAgentFromTemplateTool(Mock.Of<ILogger<CreateAgentFromTemplateTool>>(), templateService.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["template_id"] = "t",
            ["agent_name"] = "A",
            ["parent_id"] = 123
        });

        result.Success.Should().BeTrue();
        result.Output.Should().NotContain("Parent:");
        templateService.VerifyAll();
    }
}
