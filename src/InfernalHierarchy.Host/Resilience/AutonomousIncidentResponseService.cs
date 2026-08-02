using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Host.Resilience;

/// <summary>
/// Observes incident signals and applies controlled mitigations (replan/preempt/rate reduction).
/// </summary>
public sealed class AutonomousIncidentResponseService : BackgroundService
{
    private readonly IAgentSupervisor _supervisor;
    private readonly IAgentRegistry _registry;
    private readonly MetricsCollector _metrics;
    private readonly IncidentToolThrottleState _throttleState;
    private readonly IAgentEventSink? _eventSink;
    private readonly AutonomousIncidentResponseOptions _options;
    private readonly ILogger<AutonomousIncidentResponseService> _logger;

    private long _lastToolTimeoutTotal;
    private long _lastQueueRejectedTotal;
    private long _lastStalledTotal;
    private long _lastLoopingTotal;
    private bool _baselineInitialized;
    private DateTimeOffset _lastMitigationAt = DateTimeOffset.MinValue;

    public AutonomousIncidentResponseService(
        IAgentSupervisor supervisor,
        IAgentRegistry registry,
        MetricsCollector metrics,
        IncidentToolThrottleState throttleState,
        IOptions<AutonomousIncidentResponseOptions> options,
        ILogger<AutonomousIncidentResponseService> logger,
        IAgentEventSink? eventSink = null)
    {
        _supervisor = supervisor;
        _registry = registry;
        _metrics = metrics;
        _throttleState = throttleState;
        _eventSink = eventSink;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteCycleAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var toolTimeoutTotal = _metrics.GetCounter("tools.timeout.total");
        var queueRejectedTotal = (long)Math.Max(0, _metrics.GetGauge("message_bus.messages.rejected"));
        var stalledTotal = _metrics.GetCounter("supervisor.detected.stalled");
        var loopingTotal = _metrics.GetCounter("supervisor.detected.looping");

        if (!_baselineInitialized)
        {
            _lastToolTimeoutTotal = toolTimeoutTotal;
            _lastQueueRejectedTotal = queueRejectedTotal;
            _lastStalledTotal = stalledTotal;
            _lastLoopingTotal = loopingTotal;
            _baselineInitialized = true;
            return;
        }

        var timeoutDelta = ClampDelta(toolTimeoutTotal - _lastToolTimeoutTotal);
        var rejectDelta = ClampDelta(queueRejectedTotal - _lastQueueRejectedTotal);
        var stalledDelta = ClampDelta(stalledTotal - _lastStalledTotal);
        var loopingDelta = ClampDelta(loopingTotal - _lastLoopingTotal);

        _lastToolTimeoutTotal = toolTimeoutTotal;
        _lastQueueRejectedTotal = queueRejectedTotal;
        _lastStalledTotal = stalledTotal;
        _lastLoopingTotal = loopingTotal;

        if (!IsCooldownElapsed(now))
        {
            return;
        }

        if (timeoutDelta >= Math.Max(1, _options.ToolTimeoutSpikeThreshold))
        {
            await ApplyMitigationAsync(
                now,
                trigger: "tool_timeout_spike",
                reasonCode: "tool_timeout_spike",
                reason: $"Tool timeout spike detected (+{timeoutDelta} within one monitoring interval)",
                requireRateReduction: true,
                requestReplan: true,
                preemptAgentId: null,
                ct: ct).ConfigureAwait(false);
            return;
        }

        if (rejectDelta >= Math.Max(1, _options.QueueRejectGrowthThreshold))
        {
            await ApplyMitigationAsync(
                now,
                trigger: "queue_rejection_growth",
                reasonCode: "queue_rejection_growth",
                reason: $"Queue rejection growth detected (+{rejectDelta} within one monitoring interval)",
                requireRateReduction: true,
                requestReplan: true,
                preemptAgentId: null,
                ct: ct).ConfigureAwait(false);
            return;
        }

        if (loopingDelta >= Math.Max(1, _options.LoopingBranchDetectionThreshold))
        {
            var preemptCandidate = _options.EnableBranchPreemption
                ? TrySelectBranchPreemptCandidate(_options.RootAgentId)
                : null;

            await ApplyMitigationAsync(
                now,
                trigger: "looping_branch_detection",
                reasonCode: "looping_branch_detection",
                reason: $"Looping branches detected (+{loopingDelta} within one monitoring interval)",
                requireRateReduction: false,
                requestReplan: true,
                preemptAgentId: preemptCandidate,
                ct: ct).ConfigureAwait(false);
            return;
        }

        if (stalledDelta >= Math.Max(1, _options.StalledBranchDetectionThreshold))
        {
            await ApplyMitigationAsync(
                now,
                trigger: "stalled_branch_detection",
                reasonCode: "stalled_branch_detection",
                reason: $"Stalled branches detected (+{stalledDelta} within one monitoring interval)",
                requireRateReduction: false,
                requestReplan: true,
                preemptAgentId: null,
                ct: ct).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Autonomous incident response disabled (AutonomousIncidentResponse:Enabled=false)");
            return;
        }

        _logger.LogInformation(
            "Autonomous incident response enabled | Poll={Poll} Cooldown={Cooldown} TimeoutSpikeThreshold={TimeoutThreshold} QueueRejectThreshold={RejectThreshold}",
            _options.PollInterval,
            _options.ActionCooldown,
            _options.ToolTimeoutSpikeThreshold,
            _options.QueueRejectGrowthThreshold);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Autonomous incident response cycle failed");
            }

            await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyMitigationAsync(
        DateTimeOffset now,
        string trigger,
        string reasonCode,
        string reason,
        bool requireRateReduction,
        bool requestReplan,
        string? preemptAgentId,
        CancellationToken ct)
    {
        _metrics.IncrementCounter("incident_response.actions.total");
        _metrics.IncrementCounter($"incident_response.actions.{trigger}");

        if (requireRateReduction && _options.EnableTemporaryRateReduction)
        {
            _throttleState.Activate(
                now,
                _options.RateReductionDuration,
                _options.RateReductionRetryAfterMs,
                _options.DeferredToolNames,
                reasonCode);
            _metrics.IncrementCounter("incident_response.actions.rate_reduction");
        }

        if (!string.IsNullOrWhiteSpace(preemptAgentId))
        {
            await _supervisor.PreemptAgentAsync(preemptAgentId, reason, ct).ConfigureAwait(false);
            _metrics.IncrementCounter("incident_response.actions.preempt");
        }

        if (requestReplan)
        {
            await _supervisor.RequestReplanAsync(_options.RootAgentId, reason, ct).ConfigureAwait(false);
            _metrics.IncrementCounter("incident_response.actions.replan");
        }

        _lastMitigationAt = now;

        _logger.LogWarning(
            "Autonomous incident mitigation applied | trigger={Trigger} reason_code={ReasonCode} replan={Replan} preempt={PreemptAgentId} rate_reduction={RateReduction}",
            trigger,
            reasonCode,
            requestReplan,
            preemptAgentId ?? string.Empty,
            requireRateReduction && _options.EnableTemporaryRateReduction);

        EmitMitigationEvent(trigger, reasonCode, reason, requestReplan, preemptAgentId, requireRateReduction && _options.EnableTemporaryRateReduction);
    }

    private bool IsCooldownElapsed(DateTimeOffset now)
    {
        if (_lastMitigationAt == DateTimeOffset.MinValue)
        {
            return true;
        }

        var cooldown = _options.ActionCooldown <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(1)
            : _options.ActionCooldown;

        return (now - _lastMitigationAt) >= cooldown;
    }

    private string? TrySelectBranchPreemptCandidate(string rootAgentId)
    {
        return _registry.GetAllAgents()
            .Where(a => !string.Equals(a.Id, rootAgentId, StringComparison.OrdinalIgnoreCase))
            .Where(a => a.Rank != AgentRank.Supreme)
            .Where(a => a.Status is AgentStatus.Thinking or AgentStatus.ActingWithTool or AgentStatus.Waiting)
            .OrderBy(a => a.Rank)
            .Select(a => a.Id)
            .FirstOrDefault();
    }

    private void EmitMitigationEvent(
        string trigger,
        string reasonCode,
        string reason,
        bool requestReplan,
        string? preemptAgentId,
        bool rateReductionApplied)
    {
        if (_eventSink is null || !_options.EmitAuditEvents)
        {
            return;
        }

        try
        {
            _eventSink.AppendEvent(new AgentEvent
            {
                AgentId = "system.incident-response",
                Type = EventType.DecisionMade,
                Description = "Autonomous incident mitigation applied",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "incident.response",
                    ["trigger"] = trigger,
                    ["reason_code"] = reasonCode,
                    ["reason"] = reason,
                    ["requested_replan"] = requestReplan,
                    ["preempted_agent_id"] = preemptAgentId ?? string.Empty,
                    ["temporary_rate_reduction_applied"] = rateReductionApplied,
                    ["root_agent_id"] = _options.RootAgentId
                }
            });
        }
        catch
        {
            // best-effort event emission
        }
    }

    private static long ClampDelta(long delta) => delta < 0 ? 0 : delta;
}