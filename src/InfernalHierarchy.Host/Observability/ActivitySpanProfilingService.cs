using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Observability;

internal sealed class ActivitySpanProfilingService : IHostedService, IDisposable
{
    private readonly ILogger<ActivitySpanProfilingService> _logger;
    private readonly MetricsCollector _collector;
    private ActivityListener? _listener;

    public ActivitySpanProfilingService(
        ILogger<ActivitySpanProfilingService> logger,
        MetricsCollector collector)
    {
        _logger = logger;
        _collector = collector;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_listener != null)
        {
            return Task.CompletedTask;
        }

        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                source.Name.StartsWith("System.Net.Http", StringComparison.Ordinal) ||
                source.Name.StartsWith("InfernalHierarchy", StringComparison.Ordinal),

            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,

            ActivityStopped = activity =>
            {
                try
                {
                    var metric = BuildMetricName(activity);
                    var ms = activity.Duration.TotalMilliseconds;

                    if (!double.IsNaN(ms) && ms >= 0)
                    {
                        _collector.RecordValue(metric, ms);
                    }
                }
                catch
                {
                    // best-effort
                }
            }
        };

        ActivitySource.AddActivityListener(listener);
        _listener = listener;

        _logger.LogInformation("Span profiling enabled (ActivityListener)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private static string BuildMetricName(Activity activity)
    {
        // Low-cardinality: bucket server spans by route template if available (we tag it in middleware as perf.route)
        if (activity.Source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
        {
            var method = TryGetTag(activity, "http.request.method")
                         ?? TryGetTag(activity, "http.method")
                         ?? "unknown";

            var route = TryGetTag(activity, "perf.route")
                        ?? TryGetTag(activity, "http.route")
                        ?? "unknown";

            var routeId = MetricKeyNormalizer.Normalize(route);
            var methodId = MetricKeyNormalizer.Normalize(method);

            return $"trace.span.server.{methodId}.{routeId}.ms";
        }

        // For internal/client spans, keep it simple by span display name.
        var nameId = MetricKeyNormalizer.Normalize(activity.DisplayName);
        var sourceId = MetricKeyNormalizer.Normalize(activity.Source.Name);

        return $"trace.span.{sourceId}.{nameId}.ms";
    }

    private static string? TryGetTag(Activity activity, string key)
    {
        foreach (var (k, v) in activity.TagObjects)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return v?.ToString();
            }
        }

        return null;
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
