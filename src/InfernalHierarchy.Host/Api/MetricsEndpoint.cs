using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class MetricsEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/metrics", (MetricsService metricsService) =>
        {
            var metrics = metricsService.GetAllMetrics();
            var body = PrometheusMetricsFormatter.Format(metrics);
            return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8");
        });
    }
}
