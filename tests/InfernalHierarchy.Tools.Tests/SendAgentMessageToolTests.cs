using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Tools.Agent;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public class SendAgentMessageToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenTargetAgentUnknown_ShouldFail()
    {
        var logger = new Mock<ILogger<SendAgentMessageTool>>();
        var bus = new Mock<IMessageBus>();
        var registry = new Mock<IAgentRegistry>();

        registry.Setup(r => r.GetAgent("missing")).Returns((IAgent?)null);

        var tool = new SendAgentMessageTool(logger.Object, bus.Object, registry.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to_agent_id"] = "missing",
            ["task"] = "do thing"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown agent_id");
        bus.Verify(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidAgent_ShouldPublishTask()
    {
        var logger = new Mock<ILogger<SendAgentMessageTool>>();
        var bus = new Mock<IMessageBus>();
        var registry = new Mock<IAgentRegistry>();

        var agent = new Mock<IAgent>();
        agent.SetupGet(a => a.Id).Returns("a1");
        agent.SetupGet(a => a.Name).Returns("MeteoAgent");
        agent.SetupGet(a => a.Rank).Returns(AgentRank.Worker);
        agent.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        agent.SetupGet(a => a.Persona).Returns(new Persona { Name = "MeteoAgent", DemonTitle = "", SystemPrompt = "", AvailableTools = new List<string>() });

        registry.Setup(r => r.GetAgent("a1")).Returns(agent.Object);

        AgentMessage? captured = null;
        bus.Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((m, _) => captured = m)
            .Returns(Task.CompletedTask);

        var tool = new SendAgentMessageTool(logger.Object, bus.Object, registry.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to_agent_id"] = "a1",
            ["content"] = "What is the weather in Paris tomorrow?",
            ["from_agent_id"] = "lucifer"
        });

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.ToAgentId.Should().Be("a1");
        captured.FromAgentId.Should().Be("lucifer");
        captured.Type.Should().Be(MessageType.Task);
        captured.Content.Should().Contain("weather");
    }
}
