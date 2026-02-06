using System;
using System.Diagnostics;
using System.Threading.Tasks;
using InfernalHierarchy.Host.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Infrastructure;

internal static class HostUsePipeline
{
    public static void UseRequestProfiling(WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<PerfRequestProfilingOptions>>().Value;
        if (!options.Enabled)
        {
            return;
        }

        var store = app.Services.GetRequiredService<IHttpRequestProfilingStore>();

        app.Use(async (HttpContext ctx, Func<Task> next) =>
        {
            if (!options.IncludeUiRequests
                && ctx.Request.Path.StartsWithSegments("/ui", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Skip endpoints that are very chatty and not useful for profiling.
            if (ctx.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var startUtc = DateTimeOffset.UtcNow;
            var sw = Stopwatch.StartNew();
            var profileId = Guid.NewGuid().ToString("n");

            try
            {
                ctx.Response.OnStarting(() =>
                {
                    try
                    {
                        ctx.Response.Headers["X-Request-Profile-Id"] = profileId;
                    }
                    catch
                    {
                        // best-effort only
                    }
                    return Task.CompletedTask;
                });
            }
            catch
            {
                // best-effort only
            }

            try
            {
                await next();
            }
            catch
            {
                // Ensure we don't report a misleading 200 when exceptions bubble.
                if (ctx.Response.HasStarted == false && ctx.Response.StatusCode < 400)
                {
                    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
                throw;
            }
            finally
            {
                sw.Stop();

                var endpoint = ctx.GetEndpoint();
                var routeEndpoint = endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint;
                var routeTemplate = routeEndpoint?.RoutePattern?.RawText ?? "";

                var traceId = Activity.Current?.TraceId.ToString();

                var record = new HttpRequestProfileRecord(
                    Id: profileId,
                    StartTimeUtc: startUtc,
                    DurationMs: sw.Elapsed.TotalMilliseconds,
                    Method: ctx.Request.Method ?? "UNKNOWN",
                    Path: ctx.Request.Path.ToString(),
                    RouteTemplate: routeTemplate,
                    StatusCode: ctx.Response.StatusCode,
                    TraceId: string.IsNullOrWhiteSpace(traceId) ? null : traceId);

                try
                {
                    store.Add(record);
                }
                catch
                {
                    // best-effort only
                }
            }
        });
    }

    public static void UseRequestTiming(WebApplication app)
    {
        var httpLatencyCollector = app.Services.GetRequiredService<MetricsCollector>();

        static string RoutePatternToMetricId(string? routePattern) => MetricKeyNormalizer.Normalize(routePattern);

        app.Use(async (HttpContext ctx, Func<Task> next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/ui", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await next();
            }
            finally
            {
                sw.Stop();

                var endpoint = ctx.GetEndpoint();
                var routeEndpoint = endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint;
                var routeTemplate = routeEndpoint?.RoutePattern?.RawText;

                try
                {
                    System.Diagnostics.Activity.Current?.SetTag("perf.route", routeTemplate ?? "unknown");
                }
                catch
                {
                }

                var routeId = RoutePatternToMetricId(routeTemplate);
                var method = (ctx.Request.Method ?? "UNKNOWN").ToLowerInvariant();
                var status = ctx.Response.StatusCode;

                var elapsedMs = sw.Elapsed.TotalMilliseconds;

                httpLatencyCollector.RecordValue("http.latency.ms", elapsedMs);
                httpLatencyCollector.RecordValue($"http.latency.{method}.{routeId}.ms", elapsedMs);

                httpLatencyCollector.IncrementCounter($"http.requests.{method}.{routeId}");
                httpLatencyCollector.IncrementCounter($"http.responses.{status}");
            }
        });
    }

    public static void UseWebSocketsIfEnabled(WebApplication app, WebSocketInterfaceOptions wsOptions)
    {
        if (!wsOptions.Enabled)
        {
            return;
        }

        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(wsOptions.KeepAliveSeconds)
        });
    }
}
