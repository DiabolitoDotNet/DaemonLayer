using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Service for managing federation between multiple InfernalHierarchy instances
/// </summary>
public interface IFederationService
{
    /// <summary>
    /// Registers a new federated instance
    /// </summary>
    /// <param name="instance">Instance to register</param>
    /// <param name="ct">Cancellation token</param>
    Task RegisterInstanceAsync(FederatedInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Unregisters a federated instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="ct">Cancellation token</param>
    Task UnregisterInstanceAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Gets all active federated instances
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of active instances</returns>
    Task<List<FederatedInstance>> GetActiveInstancesAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends message to specific federated instance
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <param name="ct">Cancellation token</param>
    Task SendMessageAsync(FederatedMessage message, CancellationToken ct = default);

    /// <summary>
    /// Broadcasts message to all federated instances
    /// </summary>
    /// <param name="message">Message to broadcast</param>
    /// <param name="ct">Cancellation token</param>
    Task BroadcastMessageAsync(FederatedMessage message, CancellationToken ct = default);

    /// <summary>
    /// Delegates task to best available instance based on load
    /// </summary>
    /// <param name="task">Task to delegate</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Target instance ID</returns>
    Task<string?> DelegateTaskAsync(Entities.TaskEntry task, CancellationToken ct = default);

    /// <summary>
    /// Requests collaboration across multiple instances
    /// </summary>
    /// <param name="request">Collaboration request</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Aggregated result from multiple instances</returns>
    Task<CollaborationResult> RequestCrossInstanceCollaborationAsync(
        CollaborationRequest request, 
        CancellationToken ct = default);

    /// <summary>
    /// Synchronizes memory entries across instances
    /// </summary>
    /// <param name="entries">Memory entries to sync</param>
    /// <param name="targetInstances">Target instance IDs (null for all)</param>
    /// <param name="ct">Cancellation token</param>
    Task SyncMemoryAsync(
        List<Fact> entries, 
        List<string>? targetInstances = null, 
        CancellationToken ct = default);

    /// <summary>
    /// Monitors instance health via heartbeats
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task MonitorInstanceHealthAsync(CancellationToken ct = default);

    /// <summary>
    /// Selects optimal instance for agent creation based on load
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Instance ID or null if none available</returns>
    Task<string?> SelectInstanceForAgentAsync(CancellationToken ct = default);
}
