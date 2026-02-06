using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InfernalHierarchy.Host.Infrastructure;

internal static class HostUsePipeline
{
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
