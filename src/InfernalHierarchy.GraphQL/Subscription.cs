using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.GraphQL;

/// <summary>
/// GraphQL subscription resolver for real-time updates
/// </summary>
public class Subscription
{
    /// <summary>
    /// Subscribe to agent creation events
    /// </summary>
    /// <param name="messageBus">Message bus</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async stream of agents</returns>
    [Subscribe]
    public async IAsyncEnumerable<Agent> OnAgentCreated(
        [Service] IMessageBus messageBus,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var message in messageBus.SubscribeAsync<AgentMessage>(ct).ConfigureAwait(false))
        {
            if (message.Type == MessageType.AgentCreated && message.Payload.ContainsKey("Agent"))
            {
                // In real implementation, deserialize agent from payload
                yield return new Agent 
                { 
                    Id = message.FromAgentId,
                    Name = message.Content
                };
            }
        }
    }

    /// <summary>
    /// Subscribe to task status changes
    /// </summary>
    /// <param name="messageBus">Message bus</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async stream of tasks</returns>
    [Subscribe]
    public async IAsyncEnumerable<AgentTask> OnTaskStatusChanged(
        [Service] IMessageBus messageBus,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var message in messageBus.SubscribeAsync<AgentMessage>(ct).ConfigureAwait(false))
        {
            if (message.Type == MessageType.TaskStatus && message.Payload.ContainsKey("Task"))
            {
                // In real implementation, deserialize task from payload
                yield return new AgentTask 
                { 
                    Id = message.Payload["TaskId"]?.ToString() ?? string.Empty,
                    Description = message.Content
                };
            }
        }
    }
}
