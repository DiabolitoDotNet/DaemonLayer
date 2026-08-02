using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class MetricsEndpoint
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;

        app.MapGet("/metrics", (HttpContext ctx, MetricsService metricsService) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var metrics = metricsService.GetAllMetrics();
            var body = PrometheusMetricsFormatter.Format(metrics);
            return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8");
        });
    }
}
