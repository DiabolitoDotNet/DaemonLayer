using System.Collections.Concurrent;

namespace InfernalHierarchy.Host.Observability;

internal sealed class PerfRequestProfilingOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxRecords { get; set; } = 500;
    public int RetentionMinutes { get; set; } = 30;

    /// <summary>
    /// Whether to profile requests to the embedded UI assets.
    /// </summary>
    public bool IncludeUiRequests { get; set; }
}

internal sealed record HttpRequestProfileRecord(
    string Id,
    DateTimeOffset StartTimeUtc,
    double DurationMs,
    string Method,
    string Path,
    string RouteTemplate,
    int StatusCode,
    string? TraceId);

internal sealed record HttpRequestProfilingStats(int RequestCount);

internal interface IHttpRequestProfilingStore
{
    void Add(HttpRequestProfileRecord record);
    IReadOnlyList<HttpRequestProfileRecord> GetRecent(int limit);
    HttpRequestProfileRecord? GetById(string id);
    HttpRequestProfilingStats GetStats();
    void Clear();
}

internal sealed class InMemoryHttpRequestProfilingStore : IHttpRequestProfilingStore
{
    private readonly ConcurrentQueue<HttpRequestProfileRecord> _order = new();
    private readonly ConcurrentDictionary<string, HttpRequestProfileRecord> _byId = new(StringComparer.OrdinalIgnoreCase);

    private readonly PerfRequestProfilingOptions _options;

    public InMemoryHttpRequestProfilingStore(IOptions<PerfRequestProfilingOptions> options)
    {
        _options = options.Value;
    }

    public void Add(HttpRequestProfileRecord record)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _byId[record.Id] = record;
        _order.Enqueue(record);

        TrimIfNeeded();
    }

    public IReadOnlyList<HttpRequestProfileRecord> GetRecent(int limit)
    {
        if (limit <= 0) limit = 50;
        if (limit > 200) limit = 200;

        // Snapshot by values then sort by time.
        return _byId.Values
            .OrderByDescending(x => x.StartTimeUtc)
            .Take(limit)
            .ToList();
    }

    public HttpRequestProfileRecord? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _byId.TryGetValue(id, out var record) ? record : null;
    }

    public HttpRequestProfilingStats GetStats() => new(_byId.Count);

    public void Clear()
    {
        _byId.Clear();
        while (_order.TryDequeue(out _))
        {
        }
    }

    private void TrimIfNeeded()
    {
        var max = _options.MaxRecords;
        if (max <= 0) max = 500;
        if (max > 5000) max = 5000;

        var retentionMinutes = _options.RetentionMinutes;
        if (retentionMinutes <= 0) retentionMinutes = 30;
        if (retentionMinutes > 24 * 60) retentionMinutes = 24 * 60;

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-retentionMinutes);

        // We dequeue in insertion order; best-effort trimming for both TTL and max size.
        while (_order.TryPeek(out var oldest) && (oldest.StartTimeUtc < cutoff || _order.Count > max))
        {
            if (!_order.TryDequeue(out var removed))
            {
                break;
            }

            _byId.TryRemove(removed.Id, out _);
        }
    }
}
