namespace InfernalHierarchy.Core.Interfaces;

public enum FailedOperationKind
{
    MessagePublish = 0,
    ToolExecution = 1
}

public enum FailedOperationStatus
{
    Pending = 0,
    Replayed = 1,
    ReplayFailed = 2
}

public sealed class FailedOperationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public FailedOperationKind Kind { get; set; }
    public string ReasonCode { get; set; } = "unknown";
    public string OperationName { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public string? TargetId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public int RetryBudget { get; set; } = 3;
    public int ReplayAttempts { get; set; }
    public DateTimeOffset? LastReplayAttemptAtUtc { get; set; }
    public FailedOperationStatus Status { get; set; } = FailedOperationStatus.Pending;
    public string? LastReplayError { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record FailedOperationStats(
    int Total,
    int Pending,
    int Replayed,
    int ReplayFailed);

public interface IFailedOperationStore
{
    Task RecordAsync(FailedOperationRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<FailedOperationRecord>> GetRecentAsync(int limit, bool pendingOnly, CancellationToken ct = default);

    Task<FailedOperationRecord?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<FailedOperationRecord?> TryStartReplayAsync(string id, string requestedBy, CancellationToken ct = default);

    Task MarkReplaySucceededAsync(string id, CancellationToken ct = default);

    Task MarkReplayFailedAsync(string id, string reasonCode, string? errorMessage, CancellationToken ct = default);

    FailedOperationStats GetStats();
}
