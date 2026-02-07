using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace InfernalHierarchy.Messaging.Bus;

/// <summary>
/// Channel-based message bus for inter-agent communication
/// </summary>
public class ChannelMessageBus : IMessageBus, IDisposable
{
    private readonly ILogger<ChannelMessageBus> _logger;
    private readonly Channel<AgentMessage> _broadcastChannel;
    private readonly ConcurrentDictionary<string, Channel<AgentMessage>> _agentChannels;
    private bool _disposed;

    public ChannelMessageBus(ILogger<ChannelMessageBus> logger)
    {
        _logger = logger;
        _broadcastChannel = Channel.CreateUnbounded<AgentMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        _agentChannels = new ConcurrentDictionary<string, Channel<AgentMessage>>();
    }

    public async Task PublishAsync(AgentMessage message, CancellationToken ct = default)
    {
        if (_disposed)
        {
            _logger.LogWarning("Cannot publish message: MessageBus is disposed");
            return;
        }

        _logger.LogDebug("📤 Publishing message {MessageId} from {From} to {To}",
            message.Id, message.FromAgentId, message.ToAgentId ?? "broadcast");

        if (string.IsNullOrEmpty(message.ToAgentId))
        {
            // Broadcast to all
            await _broadcastChannel.Writer.WriteAsync(message, ct);
        }
        else
        {
            // Send to specific agent
            var channel = _agentChannels.GetOrAdd(message.ToAgentId, _ =>
                Channel.CreateUnbounded<AgentMessage>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                }));

            await channel.Writer.WriteAsync(message, ct);
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

        var channel = _agentChannels.GetOrAdd(agentId, _ =>
            Channel.CreateUnbounded<AgentMessage>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            }));

        await foreach (var message in channel.Reader.ReadAllAsync(ct))
        {
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

        await foreach (var message in _broadcastChannel.Reader.ReadAllAsync(ct))
        {
            yield return message;
        }
    }

    /// <summary>
    /// Remove channel for terminated agent to free resources
    /// </summary>
    public void CleanupAgent(string agentId)
    {
        if (_agentChannels.TryRemove(agentId, out var channel))
        {
            channel.Writer.Complete();
            _logger.LogDebug("🧹 Cleaned up message channel for agent {AgentId}", agentId);
        }
    }

    /// <summary>
    /// Get count of active channels (for diagnostics)
    /// </summary>
    public int ActiveChannelCount => _agentChannels.Count;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _broadcastChannel.Writer.Complete();

        foreach (var channel in _agentChannels.Values)
        {
            channel.Writer.Complete();
        }

        _agentChannels.Clear();
        _logger.LogInformation("MessageBus disposed");
    }
}
