using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Messaging.Bus;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Messaging.Tests;

public class ChannelMessageBusTests
{
    private sealed class InMemoryFailedOperationStoreForTests : IFailedOperationStore
    {
        public List<FailedOperationRecord> Records { get; } = new();

        public Task RecordAsync(FailedOperationRecord record, CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FailedOperationRecord>> GetRecentAsync(int limit, bool pendingOnly, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedOperationRecord>>(Records);

        public Task<FailedOperationRecord?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult<FailedOperationRecord?>(Records.FirstOrDefault(r => r.Id == id));

        public Task<FailedOperationRecord?> TryStartReplayAsync(string id, string requestedBy, CancellationToken ct = default)
            => Task.FromResult<FailedOperationRecord?>(null);

        public Task MarkReplaySucceededAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkReplayFailedAsync(string id, string reasonCode, string? error, CancellationToken ct = default) => Task.CompletedTask;

        public FailedOperationStats GetStats() => new(Records.Count, Records.Count, 0, 0);
    }

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
    public async Task SubscribeToBroadcastsAsync_ShouldFanOutToAllSubscribers_ExactlyOnce()
    {
        var subscriberOneCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var subscriberTwoCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var subscriberOne = new List<AgentMessage>();
        var subscriberTwo = new List<AgentMessage>();

        var subscriberOneReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriberTwoReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscriberOneTask = Task.Run(async () =>
        {
            try
            {
                subscriberOneReady.TrySetResult(true);
                await foreach (var msg in _messageBus.SubscribeToBroadcastsAsync(subscriberOneCts.Token))
                {
                    subscriberOne.Add(msg);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        var subscriberTwoTask = Task.Run(async () =>
        {
            try
            {
                subscriberTwoReady.TrySetResult(true);
                await foreach (var msg in _messageBus.SubscribeToBroadcastsAsync(subscriberTwoCts.Token))
                {
                    subscriberTwo.Add(msg);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        await Task.WhenAll(subscriberOneReady.Task, subscriberTwoReady.Task);
        await Task.Delay(100);

        var b1 = new AgentMessage
        {
            Id = "broadcast-1",
            FromAgentId = "system",
            ToAgentId = null,
            Type = MessageType.Broadcast,
            Content = "B1"
        };

        var b2 = new AgentMessage
        {
            Id = "broadcast-2",
            FromAgentId = "system",
            ToAgentId = null,
            Type = MessageType.Broadcast,
            Content = "B2"
        };

        await _messageBus.PublishAsync(b1);
        await _messageBus.PublishAsync(b2);

        await Task.Delay(150);
        subscriberOneCts.Cancel();
        subscriberTwoCts.Cancel();
        await Task.WhenAll(subscriberOneTask, subscriberTwoTask);

        subscriberOne.Select(m => m.Id).Should().BeEquivalentTo(new[] { "broadcast-1", "broadcast-2" });
        subscriberTwo.Select(m => m.Id).Should().BeEquivalentTo(new[] { "broadcast-1", "broadcast-2" });
        subscriberOne.Should().OnlyHaveUniqueItems(m => m.Id);
        subscriberTwo.Should().OnlyHaveUniqueItems(m => m.Id);
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
    public async Task PublishAsync_ShouldKeepBroadcastAndTargetedSubscriptionsIsolated()
    {
        var targetedCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var broadcastCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var targetedMessages = new List<AgentMessage>();
        var broadcastMessages = new List<AgentMessage>();

        var targetedTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in _messageBus.SubscribeAsync("agent-a", targetedCts.Token))
                {
                    targetedMessages.Add(message);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        var broadcastTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in _messageBus.SubscribeToBroadcastsAsync(broadcastCts.Token))
                {
                    broadcastMessages.Add(message);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        await Task.Delay(100);

        await _messageBus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-a",
            Type = MessageType.Task,
            Content = "targeted"
        });

        await _messageBus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = null,
            Type = MessageType.Broadcast,
            Content = "broadcast"
        });

        await Task.Delay(100);
        targetedCts.Cancel();
        broadcastCts.Cancel();
        await Task.WhenAll(targetedTask, broadcastTask);

        targetedMessages.Should().ContainSingle(m => m.Content == "targeted");
        targetedMessages.Should().NotContain(m => m.Content == "broadcast");
        broadcastMessages.Should().ContainSingle(m => m.Content == "broadcast");
        broadcastMessages.Should().NotContain(m => m.Content == "targeted");
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

        // Let subscription setup and channel be created (can be racy on loaded CI agents).
        var started = false;
        for (var i = 0; i < 40; i++)
        {
            if (_messageBus.ActiveChannelCount == 1)
            {
                started = true;
                break;
            }

            await Task.Delay(25, cts.Token);
        }

        started.Should().BeTrue("subscription should create an agent channel before cleanup assertions");

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

    [Fact]
    public async Task PublishAsync_WithRejectPolicy_ShouldRejectWhenQueueIsFull()
    {
        var failedStore = new InMemoryFailedOperationStoreForTests();
        var bus = new ChannelMessageBus(
            Mock.Of<ILogger<ChannelMessageBus>>(),
            queueCapacity: 1,
            overflowPolicy: Core.Configuration.MessageQueueOverflowPolicy.Reject,
            failedOperations: failedStore);

        await bus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-overflow",
            Type = MessageType.Task,
            Content = "first"
        });

        var act = async () => await bus.PublishAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = "agent-overflow",
            Type = MessageType.Task,
            Content = "second"
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        bus.RejectedMessages.Should().Be(1);
        bus.TargetedQueueDepth.Should().Be(1);
        failedStore.Records.Should().ContainSingle(r =>
            r.Kind == FailedOperationKind.MessagePublish &&
            r.ReasonCode == "queue_reject");
    }

    [Fact]
    public async Task PublishAsync_WithDropOldestPolicy_ShouldKeepNewestMessage()
    {
        var bus = new ChannelMessageBus(
            Mock.Of<ILogger<ChannelMessageBus>>(),
            queueCapacity: 1,
            overflowPolicy: Core.Configuration.MessageQueueOverflowPolicy.DropOldest);

        await bus.PublishAsync(new AgentMessage
        {
            Id = "m1",
            FromAgentId = "sender",
            ToAgentId = "agent-drop",
            Type = MessageType.Task,
            Content = "old"
        });

        await bus.PublishAsync(new AgentMessage
        {
            Id = "m2",
            FromAgentId = "sender",
            ToAgentId = "agent-drop",
            Type = MessageType.Task,
            Content = "new"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var msg in bus.SubscribeAsync("agent-drop", cts.Token))
        {
            msg.Id.Should().Be("m2");
            break;
        }

        bus.DroppedMessages.Should().BeGreaterOrEqualTo(1);
        bus.TargetedQueueDepth.Should().Be(0);
    }
}
