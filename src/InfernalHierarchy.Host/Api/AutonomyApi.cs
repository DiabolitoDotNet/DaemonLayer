using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class AutonomyApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;

        app.MapGet("/api/autonomy/readiness", (HttpContext ctx, AutonomyReadinessReportStore store) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var report = store.GetCurrent();
            return Results.Ok(new
            {
                generatedAtUtc = report.GeneratedAtUtc,
                catalogVersion = report.CatalogVersion,
                allCriticalReady = report.AllCriticalReady,
                items = report.Items.Select(i => new
                {
                    capability = i.Capability,
                    ready = i.Ready,
                    toolRegistered = i.ToolRegistered,
                    configurationReady = i.ConfigurationReady,
                    reason = i.Reason,
                    configurationDependencies = i.ConfigurationDependencies
                })
            });
        });

        app.MapGet("/api/autonomy/slo", (HttpContext ctx, MetricsCollector collector) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var completionRatio = collector.GetGauge("autonomy_task_completion_ratio");
            var terminalFailureRatio = collector.GetGauge("autonomy_terminal_failure_ratio");
            var outOfScopeRatio = collector.GetGauge("autonomy_out_of_scope_ratio");
            var replaySuccessRatio = collector.GetGauge("autonomy_replay_success_ratio");

            var terminalTime = collector.GetHistogramStats("autonomy.time_to_terminal_ms");

            return Results.Ok(new
            {
                ratios = new
                {
                    autonomy_task_completion_ratio = completionRatio,
                    autonomy_terminal_failure_ratio = terminalFailureRatio,
                    autonomy_out_of_scope_ratio = outOfScopeRatio,
                    autonomy_replay_success_ratio = replaySuccessRatio
                },
                counters = new
                {
                    autonomy_task_total = collector.GetCounter("autonomy.task.total"),
                    autonomy_task_completed = collector.GetCounter("autonomy.task.completed"),
                    autonomy_terminal_failure = collector.GetCounter("autonomy.task.terminal_failure"),
                    autonomy_task_out_of_scope = collector.GetCounter("autonomy.task.out_of_scope"),
                    autonomy_replay_total = collector.GetCounter("autonomy.replay.total"),
                    autonomy_replay_success = collector.GetCounter("autonomy.replay.success")
                },
                time_to_terminal_ms = new
                {
                    count = terminalTime.Count,
                    median = terminalTime.P50,
                    p95 = terminalTime.P95
                }
            });
        });

        app.MapGet("/api/autonomy/certification-manifest", (HttpContext ctx) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            return Results.Ok(new
            {
                version = AutonomyCertificationManifest.Version,
                requirements = AutonomyCertificationManifest.Requirements.Select(r => new
                {
                    benchmarkId = r.BenchmarkId,
                    requiredCapabilities = r.RequiredCapabilities
                })
            });
        });
    }
}
