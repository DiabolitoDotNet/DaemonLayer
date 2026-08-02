using System.Collections.Concurrent;

namespace InfernalHierarchy.Host.Resilience;

internal sealed class InMemoryFailedOperationStore : IFailedOperationStore
{
    private readonly FailedOperationHandlingOptions _options;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<InMemoryFailedOperationStore> _logger;
    private readonly ConcurrentDictionary<string, FailedOperationRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _order = new();
    private readonly object _gate = new();

    public InMemoryFailedOperationStore(
        IOptions<FailedOperationHandlingOptions> options,
        MetricsCollector metrics,
        ILogger<InMemoryFailedOperationStore> logger)
    {
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
        UpdateGauges();
    }

    public Task RecordAsync(FailedOperationRecord record, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.Id))
        {
            record.Id = Guid.NewGuid().ToString("N");
        }

        if (record.RetryBudget <= 0)
        {
            record.RetryBudget = Math.Max(1, _options.ReplayRetryBudget);
        }

        record.OccurredAtUtc = record.OccurredAtUtc == default ? DateTimeOffset.UtcNow : record.OccurredAtUtc;
        record.Status = FailedOperationStatus.Pending;

        _records[record.Id] = record;
        _order.Enqueue(record.Id);

        TrimIfNeeded();

        _metrics.IncrementCounter("deadletter.created");
        _metrics.IncrementCounter($"deadletter.created.{record.Kind.ToString().ToLowerInvariant()}");
        UpdateGauges();

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FailedOperationRecord>> GetRecentAsync(int limit, bool pendingOnly, CancellationToken ct = default)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 1000);

        var snapshot = _records.Values
            .Where(r => !pendingOnly || r.Status == FailedOperationStatus.Pending)
            .OrderByDescending(r => r.OccurredAtUtc)
            .Take(effectiveLimit)
            .Select(Clone)
            .ToArray();

        return Task.FromResult<IReadOnlyList<FailedOperationRecord>>(snapshot);
    }

    public Task<FailedOperationRecord?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<FailedOperationRecord?>(null);
        }

        return Task.FromResult(_records.TryGetValue(id, out var record) ? Clone(record) : null);
    }

    public Task<FailedOperationRecord?> TryStartReplayAsync(string id, string requestedBy, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<FailedOperationRecord?>(null);
        }

        lock (_gate)
        {
            if (!_records.TryGetValue(id, out var record))
            {
                return Task.FromResult<FailedOperationRecord?>(null);
            }

            if (record.Status != FailedOperationStatus.Pending)
            {
                return Task.FromResult<FailedOperationRecord?>(null);
            }

            if (record.ReplayAttempts >= record.RetryBudget)
            {
                return Task.FromResult<FailedOperationRecord?>(null);
            }

            record.ReplayAttempts++;
            record.LastReplayAttemptAtUtc = DateTimeOffset.UtcNow;
            record.Metadata["replay_requested_by"] = string.IsNullOrWhiteSpace(requestedBy) ? "unknown" : requestedBy;

            _metrics.IncrementCounter("deadletter.replay.attempt");
            UpdateGauges();

            return Task.FromResult<FailedOperationRecord?>(Clone(record));
        }
    }

    public Task MarkReplaySucceededAsync(string id, CancellationToken ct = default)
    {
        if (_records.TryGetValue(id, out var record))
        {
            record.Status = FailedOperationStatus.Replayed;
            record.LastReplayError = null;
            _metrics.IncrementCounter("deadletter.replay.succeeded");
            UpdateGauges();
        }

        return Task.CompletedTask;
    }

    public Task MarkReplayFailedAsync(string id, string reasonCode, string? error, CancellationToken ct = default)
    {
        if (_records.TryGetValue(id, out var record))
        {
            record.Status = FailedOperationStatus.ReplayFailed;
            record.LastReplayError = string.IsNullOrWhiteSpace(error) ? reasonCode : error;
            record.Metadata["replay_failure_reason"] = reasonCode;
            _metrics.IncrementCounter("deadletter.replay.failed");
            _metrics.IncrementCounter($"deadletter.replay.failed.{reasonCode.ToLowerInvariant()}");
            UpdateGauges();
        }

        return Task.CompletedTask;
    }

    public FailedOperationStats GetStats()
    {
        var values = _records.Values;
        var total = values.Count;
        var pending = values.Count(r => r.Status == FailedOperationStatus.Pending);
        var replayed = values.Count(r => r.Status == FailedOperationStatus.Replayed);
        var replayFailed = values.Count(r => r.Status == FailedOperationStatus.ReplayFailed);
        return new FailedOperationStats(total, pending, replayed, replayFailed);
    }

    private void TrimIfNeeded()
    {
        while (_records.Count > Math.Max(100, _options.MaxEntries) && _order.TryDequeue(out var id))
        {
            _records.TryRemove(id, out _);
        }
    }

    private void UpdateGauges()
    {
        var stats = GetStats();
        _metrics.SetGauge("deadletter.total", stats.Total);
        _metrics.SetGauge("deadletter.pending", stats.Pending);
        _metrics.SetGauge("deadletter.replayed", stats.Replayed);
        _metrics.SetGauge("deadletter.replay_failed", stats.ReplayFailed);
    }

    private static FailedOperationRecord Clone(FailedOperationRecord value)
    {
        return new FailedOperationRecord
        {
            Id = value.Id,
            OccurredAtUtc = value.OccurredAtUtc,
            Kind = value.Kind,
            ReasonCode = value.ReasonCode,
            OperationName = value.OperationName,
            AgentId = value.AgentId,
            TargetId = value.TargetId,
            PayloadJson = value.PayloadJson,
            RetryBudget = value.RetryBudget,
            ReplayAttempts = value.ReplayAttempts,
            LastReplayAttemptAtUtc = value.LastReplayAttemptAtUtc,
            Status = value.Status,
            LastReplayError = value.LastReplayError,
            Metadata = new Dictionary<string, string>(value.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }
}
