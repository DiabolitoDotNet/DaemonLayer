
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Message bus for inter-agent communication.
/// Implementations are typically Channel-based to provide backpressure and decoupling.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publishes a message to the bus.
    /// If <see cref="AgentMessage.ToAgentId"/> is null, the message is treated as a broadcast.
    /// </summary>
    Task PublishAsync(AgentMessage message, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to messages targeted to a specific agent.
    /// The returned stream completes when the subscription is cancelled.
    /// </summary>
    IAsyncEnumerable<AgentMessage> SubscribeAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to broadcast messages.
    /// Broadcast semantics are implementation-defined but typically mean <see cref="AgentMessage.ToAgentId"/> is null.
    /// </summary>
    IAsyncEnumerable<AgentMessage> SubscribeToBroadcastsAsync(CancellationToken ct = default);
}
