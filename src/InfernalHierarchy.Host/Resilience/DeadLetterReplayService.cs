using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Serialization;

namespace InfernalHierarchy.Host.Resilience;

internal sealed class DeadLetterReplayService
{
    private readonly IFailedOperationStore _store;
    private readonly IMessageBus _messageBus;
    private readonly IToolRegistry _toolRegistry;
    private readonly IAgentEventSink? _eventSink;
    private readonly ILogger<DeadLetterReplayService> _logger;

    public DeadLetterReplayService(
        IFailedOperationStore store,
        IMessageBus messageBus,
        IToolRegistry toolRegistry,
        IAgentEventSink? eventSink,
        ILogger<DeadLetterReplayService> logger)
    {
        _store = store;
        _messageBus = messageBus;
        _toolRegistry = toolRegistry;
        _eventSink = eventSink;
        _logger = logger;
    }

    public async Task<DeadLetterReplayResult> ReplayAsync(string id, string requestedBy, CancellationToken ct = default)
    {
        var candidate = await _store.TryStartReplayAsync(id, requestedBy, ct).ConfigureAwait(false);
        if (candidate is null)
        {
            return DeadLetterReplayResult.NotAvailable(id);
        }

        EmitReplayEvent(candidate, status: "started", requestedBy, reasonCode: null, error: null);

        try
        {
            switch (candidate.Kind)
            {
                case FailedOperationKind.MessagePublish:
                {
                    var message = JsonSerializer.Deserialize<AgentMessage>(candidate.PayloadJson, JsonDefaults.Web);
                    if (message is null)
                    {
                        await _store.MarkReplayFailedAsync(candidate.Id, "deserialize_failed", "Message payload is empty", ct).ConfigureAwait(false);
                        EmitReplayEvent(candidate, status: "failed", requestedBy, reasonCode: "deserialize_failed", error: "Message payload is empty");
                        return DeadLetterReplayResult.Failed(candidate.Id, "deserialize_failed");
                    }

                    await _messageBus.PublishAsync(message, ct).ConfigureAwait(false);
                    await _store.MarkReplaySucceededAsync(candidate.Id, ct).ConfigureAwait(false);
                    EmitReplayEvent(candidate, status: "succeeded", requestedBy, reasonCode: null, error: null);
                    return DeadLetterReplayResult.Success(candidate.Id);
                }

                case FailedOperationKind.ToolExecution:
                {
                    var payload = JsonSerializer.Deserialize<ToolReplayPayload>(candidate.PayloadJson, JsonDefaults.Web);
                    if (payload is null || string.IsNullOrWhiteSpace(payload.ToolName))
                    {
                        await _store.MarkReplayFailedAsync(candidate.Id, "deserialize_failed", "Tool replay payload invalid", ct).ConfigureAwait(false);
                        EmitReplayEvent(candidate, status: "failed", requestedBy, reasonCode: "deserialize_failed", error: "Tool replay payload invalid");
                        return DeadLetterReplayResult.Failed(candidate.Id, "deserialize_failed");
                    }

                    var result = await _toolRegistry.ExecuteToolWithTrackingAsync(
                        payload.ToolName,
                        payload.Parameters ?? new Dictionary<string, object>(),
                        agentId: FailedOperationReplayConstants.ReplayAgentId,
                        agentRank: payload.AgentRank,
                        agentName: payload.AgentName,
                        ct: ct).ConfigureAwait(false);

                    if (!result.Success)
                    {
                        var reason = "tool_result_failed";
                        await _store.MarkReplayFailedAsync(candidate.Id, reason, result.Error, ct).ConfigureAwait(false);
                        EmitReplayEvent(candidate, status: "failed", requestedBy, reasonCode: reason, error: result.Error);
                        return DeadLetterReplayResult.Failed(candidate.Id, reason, result.Error);
                    }

                    await _store.MarkReplaySucceededAsync(candidate.Id, ct).ConfigureAwait(false);
                    EmitReplayEvent(candidate, status: "succeeded", requestedBy, reasonCode: null, error: null);
                    return DeadLetterReplayResult.Success(candidate.Id);
                }

                default:
                    await _store.MarkReplayFailedAsync(candidate.Id, "unsupported_kind", $"Unsupported kind {candidate.Kind}", ct).ConfigureAwait(false);
                    EmitReplayEvent(candidate, status: "failed", requestedBy, reasonCode: "unsupported_kind", error: $"Unsupported kind {candidate.Kind}");
                    return DeadLetterReplayResult.Failed(candidate.Id, "unsupported_kind");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dead-letter replay failed for {DeadLetterId}", candidate.Id);
            await _store.MarkReplayFailedAsync(candidate.Id, "replay_exception", ex.Message, ct).ConfigureAwait(false);
            EmitReplayEvent(candidate, status: "failed", requestedBy, reasonCode: "replay_exception", error: ex.Message);
            return DeadLetterReplayResult.Failed(candidate.Id, "replay_exception", ex.Message);
        }
    }

    private void EmitReplayEvent(
        FailedOperationRecord record,
        string status,
        string requestedBy,
        string? reasonCode,
        string? error)
    {
        if (_eventSink is null)
        {
            return;
        }

        try
        {
            _eventSink.AppendEvent(new AgentEvent
            {
                AgentId = FailedOperationReplayConstants.ReplayAgentId,
                Type = EventType.DecisionMade,
                Description = "Dead-letter replay",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "deadletter.replay",
                    ["status"] = status,
                    ["deadletter_id"] = record.Id,
                    ["deadletter_kind"] = record.Kind.ToString(),
                    ["operation_name"] = record.OperationName,
                    ["retry_budget"] = record.RetryBudget,
                    ["replay_attempts"] = record.ReplayAttempts,
                    ["requested_by"] = requestedBy,
                    ["reason_code"] = reasonCode ?? string.Empty,
                    ["error"] = error ?? string.Empty,
                }
            });
        }
        catch
        {
            // best-effort eventing only
        }
    }

}

internal sealed class DeadLetterReplayResult
{
    public string DeadLetterId { get; private set; } = string.Empty;
    public bool Succeeded { get; private set; }
    public bool Available { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? Error { get; private set; }

    public static DeadLetterReplayResult NotAvailable(string id) =>
        new()
        {
            DeadLetterId = id,
            Available = false,
            Succeeded = false,
            ReasonCode = "not_available"
        };

    public static DeadLetterReplayResult Success(string id) =>
        new()
        {
            DeadLetterId = id,
            Available = true,
            Succeeded = true
        };

    public static DeadLetterReplayResult Failed(string id, string reasonCode, string? error = null) =>
        new()
        {
            DeadLetterId = id,
            Available = true,
            Succeeded = false,
            ReasonCode = reasonCode,
            Error = error
        };
}
