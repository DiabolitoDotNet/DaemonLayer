using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Serialization;

namespace InfernalHierarchy.Host.Resilience;

internal sealed class DeadLetterReplayService
{
    private readonly IFailedOperationStore _store;
    private readonly IMessageBus _messageBus;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<DeadLetterReplayService> _logger;

    public DeadLetterReplayService(
        IFailedOperationStore store,
        IMessageBus messageBus,
        IToolRegistry toolRegistry,
        ILogger<DeadLetterReplayService> logger)
    {
        _store = store;
        _messageBus = messageBus;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public async Task<DeadLetterReplayResult> ReplayAsync(string id, string requestedBy, CancellationToken ct = default)
    {
        var candidate = await _store.TryStartReplayAsync(id, requestedBy, ct).ConfigureAwait(false);
        if (candidate is null)
        {
            return DeadLetterReplayResult.NotAvailable(id);
        }

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
                        return DeadLetterReplayResult.Failed(candidate.Id, "deserialize_failed");
                    }

                    await _messageBus.PublishAsync(message, ct).ConfigureAwait(false);
                    await _store.MarkReplaySucceededAsync(candidate.Id, ct).ConfigureAwait(false);
                    return DeadLetterReplayResult.Success(candidate.Id);
                }

                case FailedOperationKind.ToolExecution:
                {
                    var payload = JsonSerializer.Deserialize<ToolReplayPayload>(candidate.PayloadJson, JsonDefaults.Web);
                    if (payload is null || string.IsNullOrWhiteSpace(payload.ToolName))
                    {
                        await _store.MarkReplayFailedAsync(candidate.Id, "deserialize_failed", "Tool replay payload invalid", ct).ConfigureAwait(false);
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
                        return DeadLetterReplayResult.Failed(candidate.Id, reason, result.Error);
                    }

                    await _store.MarkReplaySucceededAsync(candidate.Id, ct).ConfigureAwait(false);
                    return DeadLetterReplayResult.Success(candidate.Id);
                }

                default:
                    await _store.MarkReplayFailedAsync(candidate.Id, "unsupported_kind", $"Unsupported kind {candidate.Kind}", ct).ConfigureAwait(false);
                    return DeadLetterReplayResult.Failed(candidate.Id, "unsupported_kind");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dead-letter replay failed for {DeadLetterId}", candidate.Id);
            await _store.MarkReplayFailedAsync(candidate.Id, "replay_exception", ex.Message, ct).ConfigureAwait(false);
            return DeadLetterReplayResult.Failed(candidate.Id, "replay_exception", ex.Message);
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
