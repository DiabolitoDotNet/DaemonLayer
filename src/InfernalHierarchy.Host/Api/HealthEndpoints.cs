using InfernalHierarchy.Core.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace InfernalHierarchy.Host.Api;

internal static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthReportAsync
        });

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check =>
                check.Tags.Contains("external") ||
                check.Tags.Contains("database") ||
                check.Tags.Contains("storage") ||
                check.Tags.Contains("embeddings"),
            ResponseWriter = WriteHealthReportAsync
        });
    }

    private static Task WriteHealthReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    status = kvp.Value.Status.ToString(),
                    description = kvp.Value.Description,
                    durationMs = kvp.Value.Duration.TotalMilliseconds,
                    data = kvp.Value.Data
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonDefaults.WebIndented));
    }
}
