using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using InfernalHierarchy.Core.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Observability;

internal sealed class TraceCaptureOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxTraces { get; set; } = 200;
    public int MaxSpansPerTrace { get; set; } = 500;
    public int MaxTagsPerSpan { get; set; } = 48;
    public int MaxTagValueLength { get; set; } = 256;
}

internal sealed record TraceSpanRecord(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Kind,
    DateTimeOffset StartTimeUtc,
    double DurationMs,
    string Status,
    IReadOnlyDictionary<string, string> Tags);

internal sealed record TraceSummaryRecord(
    string TraceId,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    double DurationMs,
    int SpanCount,
    int ErrorCount,
    string? RootSpanName);

internal sealed record TraceDetailRecord(
    string TraceId,
    TraceSummaryRecord Summary,
    IReadOnlyList<TraceSpanRecord> Spans);

internal interface ITraceCaptureStore
{
    IReadOnlyList<TraceSummaryRecord> GetRecentTraces(int limit);
    TraceDetailRecord? GetTrace(string traceId);
    string ExportTraceJson(string traceId);
}

internal sealed class InMemoryTraceCaptureStore : ITraceCaptureStore
{
    private readonly TraceCaptureOptions _options;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TraceSpanRecord>> _spansByTrace = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _traceOrder = new();

    public InMemoryTraceCaptureStore(IOptions<TraceCaptureOptions> options)
    {
        _options = options.Value;
    }

    public void AddSpan(TraceSpanRecord span)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var traceSpans = _spansByTrace.GetOrAdd(span.TraceId, _ =>
        {
            _traceOrder.Enqueue(span.TraceId);
            TrimIfNeeded();
            return new ConcurrentDictionary<string, TraceSpanRecord>(StringComparer.OrdinalIgnoreCase);
        });

        traceSpans[span.SpanId] = span;

        // Best-effort per-trace limit: if the trace grows too large, stop accepting new spans.
        if (traceSpans.Count > _options.MaxSpansPerTrace)
        {
            // Keep existing; drop new ones.
            traceSpans.TryRemove(span.SpanId, out _);
        }
    }

    public IReadOnlyList<TraceSummaryRecord> GetRecentTraces(int limit)
    {
        if (limit <= 0) limit = 50;
        if (limit > 200) limit = 200;

        // Snapshot keys first.
        var traceIds = _spansByTrace.Keys.ToList();
        var summaries = new List<TraceSummaryRecord>(traceIds.Count);

        foreach (var traceId in traceIds)
        {
            if (!_spansByTrace.TryGetValue(traceId, out var spans) || spans.Count == 0)
            {
                continue;
            }

            var all = spans.Values.ToList();
            var start = all.Min(s => s.StartTimeUtc);
            var end = all.Max(s => s.StartTimeUtc.AddMilliseconds(s.DurationMs));
            var duration = (end - start).TotalMilliseconds;
            var errorCount = all.Count(s => string.Equals(s.Status, "Error", StringComparison.OrdinalIgnoreCase));

            var root = all
                .Where(s => string.IsNullOrWhiteSpace(s.ParentSpanId))
                .OrderBy(s => s.StartTimeUtc)
                .FirstOrDefault();

            summaries.Add(new TraceSummaryRecord(
                TraceId: traceId,
                StartTimeUtc: start,
                EndTimeUtc: end,
                DurationMs: duration,
                SpanCount: all.Count,
                ErrorCount: errorCount,
                RootSpanName: root?.Name));
        }

        return summaries
            .OrderByDescending(s => s.StartTimeUtc)
            .Take(limit)
            .ToList();
    }

    public TraceDetailRecord? GetTrace(string traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return null;
        }

        if (!_spansByTrace.TryGetValue(traceId, out var spans) || spans.Count == 0)
        {
            return null;
        }

        var all = spans.Values.OrderBy(s => s.StartTimeUtc).ToList();
        var start = all.Min(s => s.StartTimeUtc);
        var end = all.Max(s => s.StartTimeUtc.AddMilliseconds(s.DurationMs));
        var duration = (end - start).TotalMilliseconds;
        var errorCount = all.Count(s => string.Equals(s.Status, "Error", StringComparison.OrdinalIgnoreCase));

        var root = all
            .Where(s => string.IsNullOrWhiteSpace(s.ParentSpanId))
            .OrderBy(s => s.StartTimeUtc)
            .FirstOrDefault();

        var summary = new TraceSummaryRecord(
            TraceId: traceId,
            StartTimeUtc: start,
            EndTimeUtc: end,
            DurationMs: duration,
            SpanCount: all.Count,
            ErrorCount: errorCount,
            RootSpanName: root?.Name);

        return new TraceDetailRecord(traceId, summary, all);
    }

    public string ExportTraceJson(string traceId)
    {
        var trace = GetTrace(traceId);
        if (trace is null)
        {
            return JsonSerializer.Serialize(new { error = "not_found" }, JsonDefaults.WebIndented);
        }

        return JsonSerializer.Serialize(trace, JsonDefaults.WebIndented);
    }

    private void TrimIfNeeded()
    {
        while (_spansByTrace.Count > _options.MaxTraces && _traceOrder.TryDequeue(out var oldest))
        {
            _spansByTrace.TryRemove(oldest, out _);
        }
    }
}

internal sealed class ActivityTraceCaptureService : IHostedService, IDisposable
{
    private readonly ILogger<ActivityTraceCaptureService> _logger;
    private readonly TraceCaptureOptions _options;
    private readonly InMemoryTraceCaptureStore _store;
    private ActivityListener? _listener;

    public ActivityTraceCaptureService(
        IOptions<TraceCaptureOptions> options,
        InMemoryTraceCaptureStore store,
        ILogger<ActivityTraceCaptureService> logger)
    {
        _options = options.Value;
        _store = store;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Trace capture disabled (Perf:TraceCapture:Enabled=false)");
            return Task.CompletedTask;
        }

        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped
        };

        ActivitySource.AddActivityListener(_listener);
        _logger.LogInformation("Trace capture enabled (ActivityListener) | MaxTraces={MaxTraces} MaxSpansPerTrace={MaxSpansPerTrace}", _options.MaxTraces, _options.MaxSpansPerTrace);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void OnActivityStopped(Activity activity)
    {
        try
        {
            if (!_options.Enabled)
            {
                return;
            }

            var traceId = activity.TraceId.ToString();
            var spanId = activity.SpanId.ToString();
            var parentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString();

            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in activity.Tags)
            {
                if (tags.Count >= _options.MaxTagsPerSpan)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(key) || value is null)
                {
                    continue;
                }

                var str = value.ToString() ?? string.Empty;
                if (str.Length > _options.MaxTagValueLength)
                {
                    str = str.Substring(0, _options.MaxTagValueLength);
                }

                tags[key] = str;
            }

            var status = activity.Status switch
            {
                ActivityStatusCode.Error => "Error",
                ActivityStatusCode.Ok => "Ok",
                _ => "Unset"
            };

            var record = new TraceSpanRecord(
                TraceId: traceId,
                SpanId: spanId,
                ParentSpanId: parentSpanId,
                Name: activity.DisplayName,
                Kind: activity.Kind.ToString(),
                StartTimeUtc: activity.StartTimeUtc,
                DurationMs: activity.Duration.TotalMilliseconds,
                Status: status,
                Tags: tags);

            _store.AddSpan(record);
        }
        catch
        {
            // best-effort; never throw from listener callbacks
        }
    }

    public void Dispose()
    {
        try
        {
            _listener?.Dispose();
        }
        catch
        {
            // best-effort
        }
        finally
        {
            _listener = null;
        }
    }
}
