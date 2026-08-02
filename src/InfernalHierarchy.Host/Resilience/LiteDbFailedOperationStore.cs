using LiteDB;

namespace InfernalHierarchy.Host.Resilience;

internal sealed class LiteDbFailedOperationStore : IFailedOperationStore, IDisposable
{
    private const string CollectionName = "failed_operations";
    private static readonly HashSet<string> PermanentReplayFailureReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "deserialize_failed",
        "unsupported_kind"
    };

    private readonly FailedOperationHandlingOptions _options;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<LiteDbFailedOperationStore> _logger;
    private readonly object _gate = new();
    private readonly LiteDatabase _db;
    private readonly string _dbPath;

    private ILiteCollection<FailedOperationRecord> FailedOperations => _db.GetCollection<FailedOperationRecord>(CollectionName);

    public LiteDbFailedOperationStore(
        IOptions<FailedOperationHandlingOptions> options,
        IOptions<MemoryOptions> memoryOptions,
        MetricsCollector metrics,
        ILogger<LiteDbFailedOperationStore> logger)
    {
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;

        _dbPath = ResolveDatabasePath(_options.DatabasePath, memoryOptions.Value.DatabasePath);
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var mapper = new BsonMapper();
        mapper.Entity<FailedOperationRecord>().Id(x => x.Id);
        _db = new LiteDatabase(_dbPath, mapper);

        FailedOperations.EnsureIndex(x => x.OccurredAtUtc);
        FailedOperations.EnsureIndex(x => x.Status);
        FailedOperations.EnsureIndex(x => x.Kind);
        FailedOperations.EnsureIndex(x => x.OperationName);

        UpdateGauges();
        _logger.LogInformation("Failed operation store initialized at {Path}", _dbPath);
    }

    public Task RecordAsync(FailedOperationRecord record, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
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

            FailedOperations.Upsert(record);
            TrimIfNeeded();
        }

        _metrics.IncrementCounter("deadletter.created");
        _metrics.IncrementCounter($"deadletter.created.{record.Kind.ToString().ToLowerInvariant()}");
        UpdateGauges();

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FailedOperationRecord>> GetRecentAsync(int limit, bool pendingOnly, CancellationToken ct = default)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 1000);

        List<FailedOperationRecord> items;
        lock (_gate)
        {
            var query = FailedOperations.Query();
            if (pendingOnly)
            {
                query = query.Where(x => x.Status == FailedOperationStatus.Pending);
            }

            items = query
                .OrderByDescending(x => x.OccurredAtUtc)
                .Limit(effectiveLimit)
                .ToList()
                .Select(Clone)
                .ToList();
        }

        return Task.FromResult<IReadOnlyList<FailedOperationRecord>>(items);
    }

    public Task<FailedOperationRecord?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<FailedOperationRecord?>(null);
        }

        FailedOperationRecord? record;
        lock (_gate)
        {
            record = FailedOperations.FindById(id.Trim());
        }

        return Task.FromResult(record is null ? null : Clone(record));
    }

    public Task<FailedOperationRecord?> TryStartReplayAsync(string id, string requestedBy, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<FailedOperationRecord?>(null);
        }

        lock (_gate)
        {
            var record = FailedOperations.FindById(id.Trim());
            if (record is null)
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

            FailedOperations.Update(record);

            _metrics.IncrementCounter("deadletter.replay.attempt");
            UpdateGauges();

            return Task.FromResult<FailedOperationRecord?>(Clone(record));
        }
    }

    public Task MarkReplaySucceededAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            var record = FailedOperations.FindById(id.Trim());
            if (record is null)
            {
                return Task.CompletedTask;
            }

            record.Status = FailedOperationStatus.Replayed;
            record.LastReplayError = null;
            FailedOperations.Update(record);
        }

        _metrics.IncrementCounter("deadletter.replay.succeeded");
        UpdateGauges();
        return Task.CompletedTask;
    }

    public Task MarkReplayFailedAsync(string id, string reasonCode, string? error, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            var record = FailedOperations.FindById(id.Trim());
            if (record is null)
            {
                return Task.CompletedTask;
            }

            var hasRemainingBudget = record.ReplayAttempts < Math.Max(1, record.RetryBudget);
            var shouldRetry = hasRemainingBudget && !IsPermanentReplayFailure(reasonCode);

            record.Status = shouldRetry
                ? FailedOperationStatus.Pending
                : FailedOperationStatus.ReplayFailed;
            record.LastReplayError = string.IsNullOrWhiteSpace(error) ? reasonCode : error;
            record.Metadata["replay_failure_reason"] = reasonCode;
            FailedOperations.Update(record);
        }

        _metrics.IncrementCounter("deadletter.replay.failed");
        _metrics.IncrementCounter($"deadletter.replay.failed.{reasonCode.ToLowerInvariant()}");
        UpdateGauges();
        return Task.CompletedTask;
    }

    public FailedOperationStats GetStats()
    {
        lock (_gate)
        {
            var values = FailedOperations.FindAll().ToList();
            var total = values.Count;
            var pending = values.Count(r => r.Status == FailedOperationStatus.Pending);
            var replayed = values.Count(r => r.Status == FailedOperationStatus.Replayed);
            var replayFailed = values.Count(r => r.Status == FailedOperationStatus.ReplayFailed);
            return new FailedOperationStats(total, pending, replayed, replayFailed);
        }
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void TrimIfNeeded()
    {
        var maxEntries = Math.Max(100, _options.MaxEntries);
        var total = FailedOperations.LongCount();
        if (total <= maxEntries)
        {
            return;
        }

        var toRemove = (int)Math.Min(total - maxEntries, int.MaxValue);
        var oldest = FailedOperations.Query()
            .OrderBy(x => x.OccurredAtUtc)
            .Limit(toRemove)
            .ToList();

        foreach (var item in oldest)
        {
            FailedOperations.Delete(item.Id);
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

    private static string ResolveDatabasePath(string configuredPath, string memoryDbPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var memoryFullPath = Path.GetFullPath(memoryDbPath);
        var directory = Path.GetDirectoryName(memoryFullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = AppContext.BaseDirectory;
        }

        return Path.Combine(directory, "failed-operations.db");
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

    private static bool IsPermanentReplayFailure(string reasonCode)
    {
        return !string.IsNullOrWhiteSpace(reasonCode)
            && PermanentReplayFailureReasons.Contains(reasonCode.Trim());
    }
}
