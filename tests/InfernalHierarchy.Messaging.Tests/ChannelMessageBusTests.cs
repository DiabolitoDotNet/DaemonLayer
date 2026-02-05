using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Messaging.Bus;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Messaging.Tests;

public class ChannelMessageBusTests
{
    private readonly ChannelMessageBus _messageBus;

    public ChannelMessageBusTests()
    {
        var logger = Mock.Of<ILogger<ChannelMessageBus>>();
        _messageBus = new ChannelMessageBus(logger);
    }

    [Fact]
    public async Task PublishAsync_ShouldDeliverMessageToSubscriber()
    {
        // Arrange
        var agentId = "test-agent";
        var message = new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agentId,
            Type = MessageType.Task,
            Content = "Test message"
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receivedMessages = new List<AgentMessage>();

        // Act
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var msg in _messageBus.SubscribeAsync(agentId, cts.Token))
            {
                receivedMessages.Add(msg);
                break; // Only get first message
            }
        });

        await Task.Delay(100); // Let subscription setup
        await _messageBus.PublishAsync(message);
        await subscribeTask;

        // Assert
        receivedMessages.Should().HaveCount(1);
        receivedMessages[0].Content.Should().Be("Test message");
    }

    [Fact]
    public async Task SubscribeToBroadcastsAsync_ShouldReceiveBroadcastMessages()
    {
        // Arrange
        var broadcastMessage = new AgentMessage
        {
            FromAgentId = "system",
            ToAgentId = null, // null = broadcast
            Type = MessageType.Broadcast,
            Content = "Broadcast message"
        };

        var cts = new CancellationTokenSource();
        var receivedMessages = new List<AgentMessage>();
        var messageReceivedTcs = new TaskCompletionSource<bool>();

        // Act
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var msg in _messageBus.SubscribeToBroadcastsAsync(cts.Token))
            {
                receivedMessages.Add(msg);
                messageReceivedTcs.SetResult(true);
                break;
            }
        });

        await Task.Delay(100); // Let subscription setup
        await _messageBus.PublishAsync(broadcastMessage);

        // Wait for message to be received or timeout
        await Task.WhenAny(messageReceivedTcs.Task, Task.Delay(2000));
        cts.Cancel();

        try
        {
            await subscribeTask;
        }
        catch (OperationCanceledException) { }

        // Assert
        receivedMessages.Should().HaveCount(1);
        receivedMessages[0].Type.Should().Be(MessageType.Broadcast);
    }

    [Fact]
    public async Task PublishAsync_ShouldNotDeliverToWrongSubscriber()
    {
        // Arrange
        var message = new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-a",
            Type = MessageType.Task,
            Content = "For Agent A only"
        };

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var receivedMessages = new List<AgentMessage>();

        // Act
        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in _messageBus.SubscribeAsync("agent-b", cts.Token))
                {
                    receivedMessages.Add(msg);
                }
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(100);
        await _messageBus.PublishAsync(message);
        await subscribeTask;

        // Assert
        receivedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupAgent_ShouldCompleteAndRemoveChannel()
    {
        // Arrange
        var agentId = "agent-clean";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var receivedMessages = new List<AgentMessage>();
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var msg in _messageBus.SubscribeAsync(agentId, cts.Token))
            {
                receivedMessages.Add(msg);
            }
        }, cts.Token);

        await Task.Delay(50, cts.Token); // Let subscription setup and channel be created
        _messageBus.ActiveChannelCount.Should().Be(1);

        // Act
        await _messageBus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agentId,
            Type = MessageType.Task,
            Content = "hello"
        }, cts.Token);

        await Task.Delay(50, cts.Token); // Let message propagate
        _messageBus.CleanupAgent(agentId);

        await subscribeTask;

        // Assert
        receivedMessages.Should().ContainSingle(m => m.Content == "hello");
        _messageBus.ActiveChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_ShouldCompleteAndClearAllChannels_AndBeIdempotent()
    {
        // Arrange
        await _messageBus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-a",
            Type = MessageType.Task,
            Content = "hi"
        });

        _messageBus.ActiveChannelCount.Should().Be(1);

        // Act
        _messageBus.Dispose();
        _messageBus.Dispose(); // idempotent

        // Assert
        _messageBus.ActiveChannelCount.Should().Be(0);

        // Disposed bus should not throw on publish
        var act = async () => await _messageBus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-a",
            Type = MessageType.Task,
            Content = "ignored"
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SubscribeAsync_WhenDisposed_YieldsNoMessages()
    {
        _messageBus.Dispose();

        var received = new List<AgentMessage>();
        await foreach (var msg in _messageBus.SubscribeAsync("any", CancellationToken.None))
        {
            received.Add(msg);
        }

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeToBroadcastsAsync_WhenDisposed_YieldsNoMessages()
    {
        _messageBus.Dispose();

        var received = new List<AgentMessage>();
        await foreach (var msg in _messageBus.SubscribeToBroadcastsAsync(CancellationToken.None))
        {
            received.Add(msg);
        }

        received.Should().BeEmpty();
    }
}
