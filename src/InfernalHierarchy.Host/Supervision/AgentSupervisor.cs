using System.Collections.Concurrent;
using InfernalHierarchy.Agents.Base;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Host.Configuration;

namespace InfernalHierarchy.Host.Supervision;

/// <summary>
/// Supervisor/background manager that observes active agents and intervenes when agents appear stuck or looping.
/// </summary>
public sealed class AgentSupervisor : BackgroundService, IAgentSupervisor
{
    private sealed record Observation(
        AgentStatus LastStatus,
        DateTimeOffset LastProgressAt,
        DateTimeOffset LastDecisionAt,
        int NoProgressTicks,
        DateTimeOffset? LastInterventionAt);

    private static readonly string SupervisorId = "supervisor";

    private readonly IAgentRegistry _registry;
    private readonly IAgentFactory _agentFactory;
    private readonly IMessageBus _messageBus;
    private readonly ISharedMemory _sharedMemory;
    private readonly AgentSupervisorOptions _options;
    private readonly ILogger<AgentSupervisor> _logger;

    private readonly ConcurrentDictionary<string, Observation> _observations = new();

    public AgentSupervisor(
        IAgentRegistry registry,
        IAgentFactory agentFactory,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IOptions<AgentSupervisorOptions> options,
        ILogger<AgentSupervisor> logger)
    {
        _registry = registry;
        _agentFactory = agentFactory;
        _messageBus = messageBus;
        _sharedMemory = sharedMemory;
        _options = options.Value;
        _logger = logger;
    }

    public Task RequestReplanAsync(string rootAgentId, string reason, CancellationToken ct = default)
    {
        var message = new AgentMessage
        {
            FromAgentId = SupervisorId,
            ToAgentId = rootAgentId,
            Type = MessageType.Command,
            Content = $"SUPERVISOR_REPLAN: {reason}",
            Payload = new Dictionary<string, object>
            {
                ["supervisor_action"] = "replan",
                ["reason"] = reason,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        return _messageBus.PublishAsync(message, ct);
    }

    public Task PreemptAgentAsync(string agentId, string reason, CancellationToken ct = default)
    {
        _logger.LogWarning("🛑 Supervisor preempting agent {AgentId}: {Reason}", agentId, reason);
        return _agentFactory.TerminateAgentAsync(agentId, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("🧭 AgentSupervisor disabled (AgentSupervisor:Enabled=false)");
            return;
        }

        _logger.LogInformation(
            "🧭 AgentSupervisor enabled | Poll={Poll} | MaxStall={MaxStall} | MaxNoProgressTicks={MaxTicks} | Preempt={Preempt}",
            _options.PollInterval,
            _options.MaxStallDuration,
            _options.MaxNoProgressTicks,
            _options.PreemptEnabled);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SuperviseOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentSupervisor tick failed");
            }

            await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SuperviseOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var agents = _registry.GetAllAgents().ToList();
        if (agents.Count == 0)
        {
            return;
        }

        // Prevent unbounded growth if agents churn over time.
        var liveAgentIds = new HashSet<string>(agents.Select(a => a.Id), StringComparer.Ordinal);
        foreach (var trackedId in _observations.Keys)
        {
            if (!liveAgentIds.Contains(trackedId))
            {
                _observations.TryRemove(trackedId, out _);
            }
        }

        // Infer progress based on recent decision writes.
        var recentDecisions = await _sharedMemory.GetRecentDecisionsAsync(_options.DecisionLookbackCount, ct)
            .ConfigureAwait(false);

        var latestDecisionByAgent = recentDecisions
            .GroupBy(d => d.CreatedBy)
            .ToDictionary(g => g.Key, g => g.Max(d => (DateTimeOffset)d.CreatedAt));

        foreach (var agent in agents)
        {
            var lastDecisionAt = latestDecisionByAgent.TryGetValue(agent.Id, out var decisionAt)
                ? decisionAt
                : DateTimeOffset.MinValue;

            var previous = _observations.GetOrAdd(
                agent.Id,
                _ => new Observation(
                    LastStatus: agent.Status,
                    LastProgressAt: now,
                    LastDecisionAt: lastDecisionAt,
                    NoProgressTicks: 0,
                    LastInterventionAt: null));

            var statusChanged = agent.Status != previous.LastStatus;
            var decisionProgressed = lastDecisionAt > previous.LastDecisionAt;

            var newNoProgressTicks = previous.NoProgressTicks;
            var newLastProgressAt = previous.LastProgressAt;

            if (statusChanged || decisionProgressed)
            {
                newNoProgressTicks = 0;
                newLastProgressAt = now;
            }
            else if (agent.Status is AgentStatus.Thinking or AgentStatus.ActingWithTool or AgentStatus.Waiting)
            {
                newNoProgressTicks++;
            }

            var updated = previous with
            {
                LastStatus = agent.Status,
                LastDecisionAt = lastDecisionAt,
                NoProgressTicks = newNoProgressTicks,
                LastProgressAt = newLastProgressAt
            };

            _observations[agent.Id] = updated;

            if (agent.Status is AgentStatus.Terminated or AgentStatus.Suspended)
            {
                _observations.TryRemove(agent.Id, out _);
                continue;
            }

            var stalledFor = now - updated.LastProgressAt;
            var isNonIdle = agent.Status is AgentStatus.Thinking or AgentStatus.ActingWithTool or AgentStatus.Waiting;

            var interventionCooldownOk = updated.LastInterventionAt is null || (now - updated.LastInterventionAt) >= _options.InterventionCooldown;

            if (!isNonIdle || !interventionCooldownOk)
            {
                continue;
            }

            var isStalled = stalledFor >= _options.MaxStallDuration;
            var isLooping = updated.NoProgressTicks >= _options.MaxNoProgressTicks;

            if (!isStalled && !isLooping)
            {
                continue;
            }

            var rootId = TryFindRootAgentId(agent, agents) ?? agent.Id;

            var reason = $"Agent '{agent.Name}' ({agent.Rank}) appears stuck. Status={agent.Status}. " +
                         $"StalledFor={stalledFor.TotalSeconds:F0}s. NoProgressTicks={updated.NoProgressTicks}.";

            // Never auto-preempt the Supreme/root agent.
            if (_options.PreemptEnabled && agent.Rank != AgentRank.Supreme && isStalled)
            {
                await PreemptAgentAsync(agent.Id, reason, ct).ConfigureAwait(false);

                // Also ask the root to re-plan so the overall tree converges after pruning.
                await RequestReplanAsync(rootId, $"Preempted agent {agent.Id}. {reason}", ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "🧭 Supervisor preempted {AgentName} ({AgentId}) and requested replan from root {RootId}",
                    agent.Name,
                    agent.Id,
                    rootId);
            }
            else
            {
                await RequestReplanAsync(rootId, reason, ct).ConfigureAwait(false);
                _logger.LogWarning("🧭 Supervisor requested replan from root {RootId} due to {AgentName} ({AgentId})", rootId, agent.Name, agent.Id);
            }

            _observations[agent.Id] = (_observations[agent.Id]) with { LastInterventionAt = now };
        }
    }

    private static string? TryFindRootAgentId(IAgent agent, List<IAgent> allAgents)
    {
        var parentMap = allAgents
            .OfType<BaseAgent>()
            .ToDictionary(a => a.Id, a => a.ParentAgentId);

        var current = agent.Id;
        var safety = 0;

        while (safety++ < 32 && parentMap.TryGetValue(current, out var parentId) && !string.IsNullOrWhiteSpace(parentId))
        {
            current = parentId!;
        }

        return current;
    }
}
