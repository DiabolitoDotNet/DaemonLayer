using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Agents;

/// <summary>
/// Projects agent status-change broadcast events into shared memory and optionally forwards them to Lucifer
/// so the Supreme agent can react proactively.
/// </summary>
public sealed class AgentStatusChangeProjectionService : BackgroundService
{
    private const string EventName = "agent_status_changed";
    private const string MemoryCategory = "agent_status_change";

    private readonly IMessageBus _messageBus;
    private readonly ISharedMemory _sharedMemory;
    private readonly IAgentRegistry _agentRegistry;
    private readonly HierarchyOptions _hierarchyOptions;
    private readonly ILogger<AgentStatusChangeProjectionService> _logger;

    public AgentStatusChangeProjectionService(
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IAgentRegistry agentRegistry,
        IOptions<HierarchyOptions> hierarchyOptions,
        ILogger<AgentStatusChangeProjectionService> logger)
    {
        _messageBus = messageBus;
        _sharedMemory = sharedMemory;
        _agentRegistry = agentRegistry;
        _hierarchyOptions = hierarchyOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🛰️ AgentStatusChangeProjectionService started");

        await foreach (var message in _messageBus.SubscribeToBroadcastsAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!IsAgentStatusChanged(message))
            {
                continue;
            }

            await ProjectToSharedMemoryAsync(message, stoppingToken).ConfigureAwait(false);
            await ForwardToLuciferAsync(message, stoppingToken).ConfigureAwait(false);
            await ForwardToSupervisorAsync(message, stoppingToken).ConfigureAwait(false);
        }
    }

    private static bool IsAgentStatusChanged(AgentMessage message)
    {
        if (message.Payload == null)
        {
            return false;
        }

        if (message.Payload.TryGetValue("event", out var evt) &&
            string.Equals(evt?.ToString(), EventName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private async Task ProjectToSharedMemoryAsync(AgentMessage message, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                event_name = EventName,
                agent_id = TryGetPayloadString(message, "agent_id") ?? message.FromAgentId,
                agent_name = TryGetPayloadString(message, "agent_name"),
                agent_rank = TryGetPayloadString(message, "agent_rank"),
                from_status = TryGetPayloadString(message, "from_status"),
                to_status = TryGetPayloadString(message, "to_status"),
                reason = TryGetPayloadString(message, "reason"),
                utc = TryGetPayloadString(message, "utc") ?? message.Timestamp.ToString("O")
            });

            var fact = new Fact
            {
                Category = MemoryCategory,
                Content = json,
                Source = "message_bus_projection",
                Confidence = 1.0,
                CreatedBy = "system",
                Visibility = MemoryVisibility.RankBased,
                MinimumRankToView = AgentRank.Duke,
                CreatedAt = DateTime.UtcNow,
                LastModifiedAt = DateTime.UtcNow,
                LastModifiedBy = "system"
            };

            await _sharedMemory.AddFactAsync(fact, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // stopping
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to project agent status change to shared memory");
        }
    }

    private async Task ForwardToLuciferAsync(AgentMessage message, CancellationToken ct)
    {
        try
        {
            var lucifer = _agentRegistry
                .GetAgentsByRank(AgentRank.Supreme)
                .FirstOrDefault(a => string.Equals(a.Name, _hierarchyOptions.MainAgentName, StringComparison.OrdinalIgnoreCase));

            if (lucifer == null)
            {
                return;
            }

            var agentId = TryGetPayloadString(message, "agent_id") ?? message.FromAgentId;
            if (string.Equals(agentId, lucifer.Id, StringComparison.OrdinalIgnoreCase))
            {
                return; // don't forward Lucifer's own status changes back to him
            }

            var forward = new AgentMessage
            {
                FromAgentId = "system",
                ToAgentId = lucifer.Id,
                Type = MessageType.Notification,
                Content = message.Content,
                Payload = new Dictionary<string, object>(message.Payload ?? new Dictionary<string, object>())
                {
                    ["projection"] = "AgentStatusChangeProjectionService"
                },
                Timestamp = DateTime.UtcNow
            };

            await _messageBus.PublishAsync(forward, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // stopping
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to forward agent status change to Lucifer");
        }
    }

    private async Task ForwardToSupervisorAsync(AgentMessage message, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_hierarchyOptions.SupervisorAgentName))
            {
                return;
            }

            var supervisor = _agentRegistry
                .GetAllAgents()
                .FirstOrDefault(a => string.Equals(a.Name, _hierarchyOptions.SupervisorAgentName, StringComparison.OrdinalIgnoreCase));

            if (supervisor == null)
            {
                return;
            }

            var agentId = TryGetPayloadString(message, "agent_id") ?? message.FromAgentId;
            if (string.Equals(agentId, supervisor.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var forward = new AgentMessage
            {
                FromAgentId = "system",
                ToAgentId = supervisor.Id,
                Type = MessageType.Notification,
                Content = message.Content,
                Payload = new Dictionary<string, object>(message.Payload ?? new Dictionary<string, object>())
                {
                    ["projection"] = "AgentStatusChangeProjectionService"
                },
                Timestamp = DateTime.UtcNow
            };

            await _messageBus.PublishAsync(forward, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // stopping
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to forward agent status change to supervisor");
        }
    }

    private static string? TryGetPayloadString(AgentMessage message, string key)
    {
        if (message.Payload == null)
        {
            return null;
        }

        return message.Payload.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
