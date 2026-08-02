using System.Collections.Frozen;

namespace InfernalHierarchy.Host.Security;

public sealed class IncidentToolThrottleState
{
    private readonly object _gate = new();
    private DateTimeOffset _activeUntilUtc = DateTimeOffset.MinValue;
    private int _retryAfterMs;
    private string _reason = string.Empty;
    private FrozenSet<string> _deferredToolNames = FrozenSet.ToFrozenSet(Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

    public void Activate(
        DateTimeOffset now,
        TimeSpan duration,
        int retryAfterMs,
        IEnumerable<string> deferredToolNames,
        string reason)
    {
        var until = now.Add(duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : duration);

        lock (_gate)
        {
            _activeUntilUtc = until;
            _retryAfterMs = Math.Max(100, retryAfterMs);
            _reason = string.IsNullOrWhiteSpace(reason) ? "incident_mitigation" : reason.Trim();
            _deferredToolNames = deferredToolNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool TryGetActiveThrottle(DateTimeOffset now, out IncidentThrottleSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_activeUntilUtc <= now)
            {
                snapshot = IncidentThrottleSnapshot.Inactive;
                return false;
            }

            snapshot = new IncidentThrottleSnapshot(
                IsActive: true,
                ActiveUntilUtc: _activeUntilUtc,
                RetryAfterMs: _retryAfterMs,
                Reason: _reason,
                DeferredToolNames: _deferredToolNames);
            return true;
        }
    }
}

public readonly record struct IncidentThrottleSnapshot(
    bool IsActive,
    DateTimeOffset ActiveUntilUtc,
    int RetryAfterMs,
    string Reason,
    FrozenSet<string> DeferredToolNames)
{
    public static IncidentThrottleSnapshot Inactive { get; } = new(
        IsActive: false,
        ActiveUntilUtc: DateTimeOffset.MinValue,
        RetryAfterMs: 0,
        Reason: string.Empty,
        DeferredToolNames: FrozenSet.ToFrozenSet(Array.Empty<string>(), StringComparer.OrdinalIgnoreCase));
}