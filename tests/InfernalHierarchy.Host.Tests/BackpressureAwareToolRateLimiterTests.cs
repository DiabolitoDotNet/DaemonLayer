using FluentAssertions;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Messaging.Bus;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class BackpressureAwareToolRateLimiterTests
{
    [Fact]
    public async Task Check_WhenBackpressureActive_AndToolDeferred_ShouldDeny()
    {
        var bus = new ChannelMessageBus(
            Mock.Of<ILogger<ChannelMessageBus>>(),
            queueCapacity: 4,
            overflowPolicy: MessageQueueOverflowPolicy.Block,
            backpressureOptions: new MessageBusBackpressureOptions
            {
                Enabled = true,
                HighWatermarkRatio = 0.5,
                RecoverWatermarkRatio = 0.25,
                DeferCollaborationRequests = true
            });

        await bus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-a",
            Type = MessageType.Task,
            Content = "fill"
        });

        await bus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-a",
            Type = MessageType.Task,
            Content = "fill-2"
        });

        bus.IsBackpressureActive.Should().BeTrue();

        var inner = new FixedWindowToolRateLimiter(
            Options.Create(new ToolRateLimitingOptions { Enabled = false }));

        var limiter = new BackpressureAwareToolRateLimiter(
            bus,
            inner,
            Options.Create(new MessageBusOptions
            {
                Backpressure = new MessageBusBackpressureOptions
                {
                    Enabled = true,
                    DeferToolExecutions = true,
                    ToolRetryAfterMs = 1200,
                    DeferredToolNames = ["request_collaboration"]
                }
            }),
            NullLogger<BackpressureAwareToolRateLimiter>.Instance);

        var decision = limiter.Check(BuildContext("request_collaboration"));

        decision.Allowed.Should().BeFalse();
        decision.RetryAfter.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(1200);
        decision.Reason.Should().Contain("backpressure");
    }

    [Fact]
    public async Task Check_WhenBackpressureActive_ButToolNotDeferred_ShouldAllow()
    {
        var bus = new ChannelMessageBus(
            Mock.Of<ILogger<ChannelMessageBus>>(),
            queueCapacity: 4,
            overflowPolicy: MessageQueueOverflowPolicy.Block,
            backpressureOptions: new MessageBusBackpressureOptions
            {
                Enabled = true,
                HighWatermarkRatio = 0.5,
                RecoverWatermarkRatio = 0.25,
                DeferCollaborationRequests = true
            });

        await bus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-a",
            Type = MessageType.Task,
            Content = "fill"
        });

        await bus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-a",
            Type = MessageType.Task,
            Content = "fill-2"
        });

        bus.IsBackpressureActive.Should().BeTrue();

        var inner = new FixedWindowToolRateLimiter(
            Options.Create(new ToolRateLimitingOptions { Enabled = false }));

        var limiter = new BackpressureAwareToolRateLimiter(
            bus,
            inner,
            Options.Create(new MessageBusOptions
            {
                Backpressure = new MessageBusBackpressureOptions
                {
                    Enabled = true,
                    DeferToolExecutions = true,
                    DeferredToolNames = ["request_collaboration"]
                }
            }),
            NullLogger<BackpressureAwareToolRateLimiter>.Instance);

        var decision = limiter.Check(BuildContext("web_search"));

        decision.Allowed.Should().BeTrue();
    }

    private static ToolExecutionContext BuildContext(string toolName)
    {
        var tool = new Mock<ITool>();
        tool.SetupGet(t => t.Name).Returns(toolName);

        return new ToolExecutionContext(
            ToolName: toolName,
            Tool: tool.Object,
            Parameters: new Dictionary<string, object>(),
            AgentId: "lucifer",
            AgentRank: "Supreme",
            CancellationToken: CancellationToken.None,
            AgentName: "Lucifer");
    }
}
