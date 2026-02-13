using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Interfaces;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class DefaultActionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenToolNotAllowed_ReturnsNotAllowedAndDoesNotExecute()
    {
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var executor = new DefaultActionExecutor(new DefaultActionInputParser(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));

        var result = await executor.ExecuteAsync(new ActionExecutionContext(
            ToolRegistry: registry.Object,
            ToolName: "prompt_ab_test",
            ActionInputText: "{}",
            ActionInputObject: null,
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: "Supreme",
            AvailableTools: ["email_send"],
            CancellationToken: CancellationToken.None));

        result.ToolFound.Should().BeFalse();
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not allowed");
        result.Observation.Should().Contain("not allowed");

        registry.Verify(r => r.GetTool(It.IsAny<string>()), Times.Never);
        registry.Verify(r => r.ExecuteToolWithTrackingAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenToolAllowed_ExecutesViaRegistryAndReturnsObservation()
    {
        var tool = Mock.Of<ITool>();

        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetTool("email_send")).Returns(tool);
        registry
            .Setup(r => r.ExecuteToolWithTrackingAsync(
                "email_send",
                It.IsAny<Dictionary<string, object>>(),
                "lucifer",
                "Supreme",
                "Lucifer",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "Email sent" });

        var executor = new DefaultActionExecutor(new DefaultActionInputParser(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));

        var result = await executor.ExecuteAsync(new ActionExecutionContext(
            ToolRegistry: registry.Object,
            ToolName: "email_send",
            ActionInputText: "{}",
            ActionInputObject: new Dictionary<string, object> { ["subject"] = "s", ["message"] = "b" },
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: "Supreme",
            AvailableTools: ["email_send"],
            CancellationToken: CancellationToken.None));

        result.ToolFound.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Observation.Should().Contain("Email sent");

        registry.Verify(r => r.GetTool("email_send"), Times.Once);
        registry.Verify(r => r.ExecuteToolWithTrackingAsync(
            "email_send",
            It.IsAny<Dictionary<string, object>>(),
            "lucifer",
            "Supreme",
            "Lucifer",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
