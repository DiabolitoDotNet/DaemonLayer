
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Message bus for inter-agent communication using Channels
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publish a message to the bus
    /// </summary>
    Task PublishAsync(AgentMessage message, CancellationToken ct = default);

    /// <summary>
    /// Subscribe to messages for a specific agent
    /// </summary>
    IAsyncEnumerable<AgentMessage> SubscribeAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Subscribe to all broadcast messages
    /// </summary>
    IAsyncEnumerable<AgentMessage> SubscribeToBroadcastsAsync(CancellationToken ct = default);
}
