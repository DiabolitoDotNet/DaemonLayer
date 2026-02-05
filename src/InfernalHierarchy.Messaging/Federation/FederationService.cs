using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Messaging.Federation;

/// <summary>
/// Implements federation between multiple InfernalHierarchy instances using HTTP
/// </summary>
public class FederationService : IFederationService
{
    private readonly ILogger<FederationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, FederatedInstance> _instances = new();
    private readonly string _localInstanceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="httpClient">HTTP client for remote communication</param>
    /// <param name="localInstanceId">Local instance identifier</param>
    public FederationService(
        ILogger<FederationService> logger,
        HttpClient httpClient,
        string localInstanceId)
    {
        _logger = logger;
        _httpClient = httpClient;
        _localInstanceId = localInstanceId;
    }

    /// <inheritdoc/>
    public Task RegisterInstanceAsync(FederatedInstance instance, CancellationToken ct = default)
    {
        _logger.LogInformation("Registering federated instance {InstanceId} ({Name}) at {BaseUrl}",
            instance.InstanceId, instance.Name, instance.BaseUrl);

        instance.LastHeartbeat = DateTime.UtcNow;
        _instances[instance.InstanceId] = instance;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UnregisterInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Unregistering federated instance {InstanceId}", instanceId);
        _instances.TryRemove(instanceId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<List<FederatedInstance>> GetActiveInstancesAsync(CancellationToken ct = default)
    {
        var activeInstances = _instances.Values
            .Where(i => i.IsActive && (DateTime.UtcNow - i.LastHeartbeat).TotalSeconds < 60)
            .ToList();

        return Task.FromResult(activeInstances);
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(FederatedMessage message, CancellationToken ct = default)
    {
        if (!_instances.TryGetValue(message.TargetInstanceId, out var targetInstance))
        {
            _logger.LogWarning("Target instance {InstanceId} not found", message.TargetInstanceId);
            return;
        }

        _logger.LogDebug("Sending {MessageType} to instance {InstanceId}",
            message.MessageType, message.TargetInstanceId);

        try
        {
            var endpoint = $"{targetInstance.BaseUrl}/api/federation/message";
            var response = await _httpClient.PostAsJsonAsync(endpoint, message, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (message.RequiresResponse)
            {
                var result = await response.Content.ReadFromJsonAsync<FederatedMessage>(cancellationToken: ct)
                    .ConfigureAwait(false);
                _logger.LogDebug("Received response from {InstanceId}: {CorrelationId}",
                    message.TargetInstanceId, result?.CorrelationId);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send message to instance {InstanceId}", message.TargetInstanceId);
            targetInstance.IsActive = false;
        }
    }

    /// <inheritdoc/>
    public async Task BroadcastMessageAsync(FederatedMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation("Broadcasting {MessageType} to all instances", message.MessageType);

        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);
        var tasks = instances
            .Where(i => i.InstanceId != _localInstanceId)
            .Select(i =>
            {
                var msg = new FederatedMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    SourceInstanceId = message.SourceInstanceId,
                    TargetInstanceId = i.InstanceId,
                    MessageType = message.MessageType,
                    Payload = message.Payload,
                    TtlSeconds = message.TtlSeconds,
                    RequiresResponse = false
                };
                return SendMessageAsync(msg, ct);
            });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> DelegateTaskAsync(TaskEntry task, CancellationToken ct = default)
    {
        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);

        // Select instance with lowest load
        var targetInstance = instances
            .Where(i => i.InstanceId != _localInstanceId)
            .Where(i => i.CurrentAgentCount < i.MaxAgents)
            .OrderBy(i => i.CurrentLoad)
            .FirstOrDefault();

        if (targetInstance == null)
        {
            _logger.LogWarning("No available instances for task delegation");
            return null;
        }

        _logger.LogInformation("Delegating task {TaskId} to instance {InstanceId} (load: {Load:P0})",
            task.Id, targetInstance.InstanceId, targetInstance.CurrentLoad);

        var message = new FederatedMessage
        {
            SourceInstanceId = _localInstanceId,
            TargetInstanceId = targetInstance.InstanceId,
            MessageType = FederatedMessageType.DelegateTask,
            Payload = new Dictionary<string, object>
            {
                ["TaskId"] = task.Id,
                ["Task"] = JsonSerializer.Serialize(task)
            },
            RequiresResponse = true
        };

        await SendMessageAsync(message, ct).ConfigureAwait(false);
        return targetInstance.InstanceId;
    }

    /// <inheritdoc/>
    public async Task<CollaborationResult> RequestCrossInstanceCollaborationAsync(
        CollaborationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting cross-instance collaboration for task: {Task}", request.Task);

        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);
        var responses = new ConcurrentBag<AgentResponse>();

        var tasks = instances
            .Where(i => i.InstanceId != _localInstanceId)
            .Select(async instance =>
            {
                var message = new FederatedMessage
                {
                    SourceInstanceId = _localInstanceId,
                    TargetInstanceId = instance.InstanceId,
                    MessageType = FederatedMessageType.CollaborationRequest,
                    Payload = new Dictionary<string, object>
                    {
                        ["RequestId"] = request.Id,
                        ["Task"] = request.Task,
                        ["Strategy"] = request.Strategy.ToString()
                    },
                    RequiresResponse = true,
                    TtlSeconds = (int)request.Timeout.TotalSeconds
                };

                try
                {
                    await SendMessageAsync(message, ct).ConfigureAwait(false);
                    // TODO: Collect responses from instances
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get collaboration response from {InstanceId}",
                        instance.InstanceId);
                }
            });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Aggregate responses (simplified for now)
        return new CollaborationResult
        {
            Decision = "CROSS_INSTANCE_AGGREGATION",
            Confidence = 0.8,
            Responses = responses.ToList(),
            ParticipantCount = responses.Count,
            AggregatedReasoning = $"Aggregated {responses.Count} responses from {instances.Count} instances",
            Strategy = request.Strategy
        };
    }

    /// <inheritdoc/>
    public async Task SyncMemoryAsync(
        List<Fact> entries,
        List<string>? targetInstances = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Syncing {Count} memory entries to federated instances", entries.Count);

        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);
        var targets = targetInstances == null
            ? instances.Where(i => i.InstanceId != _localInstanceId).ToList()
            : instances.Where(i => targetInstances.Contains(i.InstanceId)).ToList();

        var tasks = targets.Select(instance =>
        {
            var message = new FederatedMessage
            {
                SourceInstanceId = _localInstanceId,
                TargetInstanceId = instance.InstanceId,
                MessageType = FederatedMessageType.MemorySync,
                Payload = new Dictionary<string, object>
                {
                    ["Entries"] = JsonSerializer.Serialize(entries)
                }
            };
            return SendMessageAsync(message, ct);
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task MonitorInstanceHealthAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Monitoring health of {Count} federated instances", _instances.Count);

        var instances = _instances.Values.ToList();
        var tasks = instances.Select(async instance =>
        {
            try
            {
                var message = new FederatedMessage
                {
                    SourceInstanceId = _localInstanceId,
                    TargetInstanceId = instance.InstanceId,
                    MessageType = FederatedMessageType.Heartbeat,
                    RequiresResponse = true,
                    TtlSeconds = 10
                };

                await SendMessageAsync(message, ct).ConfigureAwait(false);
                instance.LastHeartbeat = DateTime.UtcNow;
                instance.IsActive = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Instance {InstanceId} failed heartbeat", instance.InstanceId);
                instance.IsActive = false;
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Remove instances that haven't responded in 5 minutes
        var staleInstances = _instances.Values
            .Where(i => (DateTime.UtcNow - i.LastHeartbeat).TotalMinutes > 5)
            .ToList();

        foreach (var stale in staleInstances)
        {
            _logger.LogWarning("Removing stale instance {InstanceId}", stale.InstanceId);
            _instances.TryRemove(stale.InstanceId, out _);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> SelectInstanceForAgentAsync(CancellationToken ct = default)
    {
        var instances = await GetActiveInstancesAsync(ct).ConfigureAwait(false);

        var selectedInstance = instances
            .Where(i => i.CurrentAgentCount < i.MaxAgents)
            .OrderBy(i => i.CurrentLoad)
            .ThenBy(i => i.CurrentAgentCount)
            .FirstOrDefault();

        if (selectedInstance != null)
        {
            _logger.LogInformation(
                "Selected instance {InstanceId} for agent creation (load: {Load:P0}, agents: {Count}/{Max})",
                selectedInstance.InstanceId,
                selectedInstance.CurrentLoad,
                selectedInstance.CurrentAgentCount,
                selectedInstance.MaxAgents);
        }

        return selectedInstance?.InstanceId;
    }
}
