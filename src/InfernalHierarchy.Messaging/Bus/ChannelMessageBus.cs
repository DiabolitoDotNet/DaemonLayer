using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using InfernalHierarchy.Core.Configuration;

namespace InfernalHierarchy.Messaging.Bus;

/// <summary>
/// Channel-based message bus for inter-agent communication
/// </summary>
public class ChannelMessageBus : IMessageBus, IDisposable
{
    private readonly ILogger<ChannelMessageBus> _logger;
    private readonly int _queueCapacity;
    private readonly MessageQueueOverflowPolicy _overflowPolicy;
    private readonly IFailedOperationStore? _failedOperations;
    private readonly ConcurrentDictionary<string, ChannelState> _broadcastSubscriberChannels;
    private readonly ConcurrentDictionary<string, ChannelState> _agentChannels;
    private long _droppedMessages;
    private long _rejectedMessages;
    private bool _disposed;

    public ChannelMessageBus(ILogger<ChannelMessageBus> logger)
        : this(logger, queueCapacity: 1000, overflowPolicy: MessageQueueOverflowPolicy.Block)
    {
    }

    public ChannelMessageBus(
        ILogger<ChannelMessageBus> logger,
        int queueCapacity,
        MessageQueueOverflowPolicy overflowPolicy,
        IFailedOperationStore? failedOperations = null)
    {
        _logger = logger;
        _queueCapacity = Math.Max(1, queueCapacity);
        _overflowPolicy = overflowPolicy;
        _failedOperations = failedOperations;
        _broadcastSubscriberChannels = new ConcurrentDictionary<string, ChannelState>();
        _agentChannels = new ConcurrentDictionary<string, ChannelState>();
    }

    public async Task PublishAsync(AgentMessage message, CancellationToken ct = default)
    {
        if (_disposed)
        {
            _logger.LogWarning("Cannot publish message: MessageBus is disposed");
            await RecordDeadLetterAsync(message, "bus_disposed", targetId: message.ToAgentId, ct).ConfigureAwait(false);
            return;
        }

        _logger.LogDebug("📤 Publishing message {MessageId} from {From} to {To}",
            message.Id, message.FromAgentId, message.ToAgentId ?? "broadcast");

        if (string.IsNullOrEmpty(message.ToAgentId))
        {
            // Broadcast fan-out: each active subscriber gets its own copy exactly once.
            foreach (var entry in _broadcastSubscriberChannels)
            {
                var enqueued = await TryEnqueueAsync(entry.Value, message, ct).ConfigureAwait(false);
                if (!enqueued)
                {
                    await RecordDeadLetterAsync(message, "broadcast_enqueue_failed", targetId: entry.Key, ct).ConfigureAwait(false);
                    _logger.LogWarning(
                        "Broadcast message {MessageId} was not enqueued for subscriber {SubscriberId}; policy={Policy}",
                        message.Id,
                        entry.Key,
                        _overflowPolicy);
                }
            }
        }
        else
        {
            // Send to specific agent
            var channel = _agentChannels.GetOrAdd(message.ToAgentId, _ =>
                CreateChannelState(singleReader: true));

            var enqueued = await TryEnqueueAsync(channel, message, ct).ConfigureAwait(false);
            if (!enqueued && _overflowPolicy == MessageQueueOverflowPolicy.Reject)
            {
                await RecordDeadLetterAsync(message, "queue_reject", targetId: message.ToAgentId, ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Message queue for agent '{message.ToAgentId}' is full (policy=Reject).");
            }
        }
    }

    public async IAsyncEnumerable<AgentMessage> SubscribeAsync(
        string agentId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_disposed)
        {
            _logger.LogWarning("Cannot subscribe: MessageBus is disposed");
            yield break;
        }

        _logger.LogInformation("🔔 Agent {AgentId} subscribing to messages", agentId);

        var channel = _agentChannels.GetOrAdd(agentId, _ => CreateChannelState(singleReader: true));

        await foreach (var message in channel.Channel.Reader.ReadAllAsync(ct))
        {
            DecrementDepth(channel);
            yield return message;
        }
    }

    public async IAsyncEnumerable<AgentMessage> SubscribeToBroadcastsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_disposed)
        {
            _logger.LogWarning("Cannot subscribe to broadcasts: MessageBus is disposed");
            yield break;
        }

        _logger.LogInformation("📡 Subscribing to broadcast messages");

        var subscriptionId = Guid.NewGuid().ToString("N");
        var channel = CreateChannelState(singleReader: true);

        _broadcastSubscriberChannels[subscriptionId] = channel;

        try
        {
            await foreach (var message in channel.Channel.Reader.ReadAllAsync(ct))
            {
                DecrementDepth(channel);
                yield return message;
            }
        }
        finally
        {
            if (_broadcastSubscriberChannels.TryRemove(subscriptionId, out var removed))
            {
                removed.Channel.Writer.TryComplete();
            }
        }
    }

    /// <summary>
    /// Remove channel for terminated agent to free resources
    /// </summary>
    public void CleanupAgent(string agentId)
    {
        if (_agentChannels.TryRemove(agentId, out var channel))
        {
            channel.Channel.Writer.TryComplete();
            _logger.LogDebug("🧹 Cleaned up message channel for agent {AgentId}", agentId);
        }
    }

    /// <summary>
    /// Get count of active channels (for diagnostics)
    /// </summary>
    public int ActiveChannelCount => _agentChannels.Count;

    /// <summary>
    /// Current number of active broadcast subscribers.
    /// </summary>
    public int ActiveBroadcastSubscriberCount => _broadcastSubscriberChannels.Count;

    /// <summary>
    /// Total queued messages across targeted channels.
    /// </summary>
    public int TargetedQueueDepth => _agentChannels.Values.Sum(static c => c.Depth);

    /// <summary>
    /// Total queued messages across broadcast subscriber channels.
    /// </summary>
    public int BroadcastQueueDepth => _broadcastSubscriberChannels.Values.Sum(static c => c.Depth);

    /// <summary>
    /// Number of dropped messages due to overflow policy.
    /// </summary>
    public long DroppedMessages => Interlocked.Read(ref _droppedMessages);

    /// <summary>
    /// Number of rejected messages due to overflow policy.
    /// </summary>
    public long RejectedMessages => Interlocked.Read(ref _rejectedMessages);

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        foreach (var channel in _broadcastSubscriberChannels.Values)
        {
            channel.Channel.Writer.TryComplete();
        }

        _broadcastSubscriberChannels.Clear();

        foreach (var channel in _agentChannels.Values)
        {
            channel.Channel.Writer.TryComplete();
        }

        _agentChannels.Clear();
        _logger.LogInformation("MessageBus disposed");
    }

    private ChannelState CreateChannelState(bool singleReader)
    {
        var boundedOptions = new BoundedChannelOptions(_queueCapacity)
        {
            SingleReader = singleReader,
            SingleWriter = false,
            FullMode = _overflowPolicy switch
            {
                MessageQueueOverflowPolicy.Block => BoundedChannelFullMode.Wait,
                MessageQueueOverflowPolicy.DropOldest => BoundedChannelFullMode.DropOldest,
                MessageQueueOverflowPolicy.Reject => BoundedChannelFullMode.DropWrite,
                _ => BoundedChannelFullMode.Wait
            }
        };

        return new ChannelState(Channel.CreateBounded<AgentMessage>(boundedOptions));
    }

    private async Task<bool> TryEnqueueAsync(ChannelState state, AgentMessage message, CancellationToken ct)
    {
        if (_overflowPolicy == MessageQueueOverflowPolicy.Reject)
        {
            if (Volatile.Read(ref state.Depth) >= _queueCapacity)
            {
                Interlocked.Increment(ref _rejectedMessages);
                return false;
            }

            if (!state.Channel.Writer.TryWrite(message))
            {
                Interlocked.Increment(ref _rejectedMessages);
                return false;
            }

            IncrementDepth(state, wasAtCapacity: false);
            return true;
        }

        if (_overflowPolicy == MessageQueueOverflowPolicy.DropOldest)
        {
            var wasAtCapacity = Volatile.Read(ref state.Depth) >= _queueCapacity;
            if (!state.Channel.Writer.TryWrite(message))
            {
                Interlocked.Increment(ref _rejectedMessages);
                return false;
            }

            if (wasAtCapacity)
            {
                Interlocked.Increment(ref _droppedMessages);
            }

            IncrementDepth(state, wasAtCapacity);
            return true;
        }

        try
        {
            await state.Channel.Writer.WriteAsync(message, ct).ConfigureAwait(false);
            IncrementDepth(state, wasAtCapacity: false);
            return true;
        }
        catch (ChannelClosedException)
        {
            Interlocked.Increment(ref _rejectedMessages);
            return false;
        }
    }

    private void IncrementDepth(ChannelState state, bool wasAtCapacity)
    {
        if (wasAtCapacity)
        {
            return;
        }

        var after = Interlocked.Increment(ref state.Depth);
        if (after > _queueCapacity)
        {
            Interlocked.Exchange(ref state.Depth, _queueCapacity);
        }
    }

    private static void DecrementDepth(ChannelState state)
    {
        while (true)
        {
            var current = Volatile.Read(ref state.Depth);
            if (current <= 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref state.Depth, current - 1, current) == current)
            {
                return;
            }
        }
    }

    private sealed class ChannelState
    {
        public ChannelState(Channel<AgentMessage> channel)
        {
            Channel = channel;
        }

        public Channel<AgentMessage> Channel { get; }

        public int Depth;
    }

    private async Task RecordDeadLetterAsync(AgentMessage message, string reasonCode, string? targetId, CancellationToken ct)
    {
        if (_failedOperations is null)
        {
            return;
        }

        try
        {
            await _failedOperations.RecordAsync(new FailedOperationRecord
            {
                Kind = FailedOperationKind.MessagePublish,
                ReasonCode = reasonCode,
                OperationName = "message_bus_publish",
                AgentId = message.FromAgentId,
                TargetId = targetId,
                PayloadJson = JsonSerializer.Serialize(message),
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["message_id"] = message.Id,
                    ["message_type"] = message.Type.ToString(),
                    ["overflow_policy"] = _overflowPolicy.ToString()
                }
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to record message dead-letter entry");
        }
    }
}
