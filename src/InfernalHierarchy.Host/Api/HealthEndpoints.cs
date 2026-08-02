using InfernalHierarchy.Core.Serialization;
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

        var failingDependencies = report.Entries
            .Where(kvp => kvp.Value.Status != HealthStatus.Healthy)
            .Select(kvp => new
            {
                name = kvp.Key,
                status = kvp.Value.Status.ToString(),
                description = kvp.Value.Description,
                hint = BuildHint(kvp.Key, kvp.Value),
                data = kvp.Value.Data
            })
            .ToList();

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            summary = new
            {
                totalChecks = report.Entries.Count,
                healthy = report.Entries.Count(e => e.Value.Status == HealthStatus.Healthy),
                degraded = report.Entries.Count(e => e.Value.Status == HealthStatus.Degraded),
                unhealthy = report.Entries.Count(e => e.Value.Status == HealthStatus.Unhealthy),
                failingDependencies
            },
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

    private static string BuildHint(string name, HealthReportEntry entry)
    {
        return name.ToLowerInvariant() switch
        {
            "ollama" => "Verify Ollama base URL, model availability, and local service reachability.",
            "qdrant" => "Verify vector memory settings and Qdrant HTTP reachability.",
            "onnx_embeddings" => "Check configured model/tokenizer asset paths and initialization logs.",
            "telegram" => "Check bot token configuration and outbound network access.",
            "voice_sidecar" => "Verify sidecar base URL and that the sidecar health endpoint is reachable.",
            "litedb" => "Check database path permissions and file integrity.",
            "agents" => "Confirm bootstrap agents started and the orchestrator completed initialization.",
            _ => entry.Description ?? "Inspect health check data for the failing dependency."
        };
    }
}
