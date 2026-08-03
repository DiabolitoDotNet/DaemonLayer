using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace InfernalHierarchy.Host.Api;

internal static class TimelineApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;

        app.MapGet("/api/perf/timeline", async (
            HttpContext ctx,
            EventStore store,
            int? minutes,
            int? limit,
            string? gapWorkflowId,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var rangeMinutes = minutes is > 0 and <= 24 * 60 ? minutes.Value : 60;
            var max = limit is > 0 and <= 2000 ? limit.Value : 500;

            var end = DateTime.UtcNow;
            var start = end.AddMinutes(-rangeMinutes);
            var events = await store.GetEventsByTimeRangeAsync(start, end, ct).ConfigureAwait(false);

            var items = events
                .Where(e => MatchesGapWorkflow(e, gapWorkflowId))
                .Where(IsTimelineRelevant)
                .Select(MapTimeline)
                .TakeLast(max)
                .ToList();

            var summary = new
            {
                items = items.Count,
                reasoning = items.Count(x => x.Kind == "reasoning"),
                tool = items.Count(x => x.Kind == "tool"),
                task = items.Count(x => x.Kind == "task"),
                gap_workflow_id = gapWorkflowId ?? string.Empty,
                terminal_state = items
                    .Select(i => TryGetMetadataString(i.Metadata, "workflow_state"))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .LastOrDefault() ?? string.Empty,
                terminal_reason_code = items
                    .Select(i => TryGetMetadataString(i.Metadata, "note"))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .LastOrDefault() ?? string.Empty
            };

            return Results.Ok(new { startUtc = start, endUtc = end, summary, items });
        });

        app.MapGet("/api/perf/timeline/explainability", async (
            HttpContext ctx,
            EventStore store,
            OperatorExplainabilityService explainability,
            int? minutes,
            int? limit,
            string? gapWorkflowId,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var rangeMinutes = minutes is > 0 and <= 24 * 60 ? minutes.Value : 60;
            var max = limit is > 0 and <= 2000 ? limit.Value : 500;

            var end = DateTime.UtcNow;
            var start = end.AddMinutes(-rangeMinutes);
            var events = await store.GetEventsByTimeRangeAsync(start, end, ct).ConfigureAwait(false);
            events = events.Where(e => MatchesGapWorkflow(e, gapWorkflowId));

            var report = explainability.BuildReport(events, max);
            return Results.Ok(new
            {
                startUtc = start,
                endUtc = end,
                gapWorkflowId,
                summary = report.Summary,
                items = report.Items
            });
        });

        app.MapGet("/api/perf/timeline/workflow/{gapWorkflowId}", async (
            HttpContext ctx,
            EventStore store,
            string gapWorkflowId,
            int? minutes,
            int? limit,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            if (string.IsNullOrWhiteSpace(gapWorkflowId))
            {
                return Results.BadRequest(new { error = "gapWorkflowId is required" });
            }

            var rangeMinutes = minutes is > 0 and <= 24 * 60 ? minutes.Value : 240;
            var max = limit is > 0 and <= 4000 ? limit.Value : 2000;

            var end = DateTime.UtcNow;
            var start = end.AddMinutes(-rangeMinutes);
            var events = await store.GetEventsByTimeRangeAsync(start, end, ct).ConfigureAwait(false);

            var items = events
                .Where(e => MatchesGapWorkflow(e, gapWorkflowId))
                .Where(IsTimelineRelevant)
                .Select(MapTimeline)
                .TakeLast(max)
                .ToList();

            return Results.Ok(new
            {
                gapWorkflowId,
                startUtc = start,
                endUtc = end,
                count = items.Count,
                items
            });
        });
    }

    private static bool MatchesGapWorkflow(AgentEvent evt, string? gapWorkflowId)
    {
        if (string.IsNullOrWhiteSpace(gapWorkflowId))
        {
            return true;
        }

        if (evt.Metadata.TryGetValue("gap_workflow_id", out var gapObj)
            && string.Equals(gapObj?.ToString(), gapWorkflowId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? TryGetMetadataString(Dictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw.ToString();
    }

    private static bool IsTimelineRelevant(AgentEvent evt)
    {
        if (evt.Type is EventType.ToolExecuted or EventType.TaskStarted or EventType.TaskCompleted or EventType.TaskFailed)
        {
            return true;
        }

        if (evt.Metadata.TryGetValue("category", out var categoryRaw)
            && categoryRaw?.ToString()?.Equals("react.checkpoint", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (evt.Type == EventType.DecisionMade)
        {
            return true;
        }

        return false;
    }

    private static TimelineItem MapTimeline(AgentEvent evt)
    {
        var category = evt.Metadata.TryGetValue("category", out var catRaw) ? catRaw?.ToString() ?? string.Empty : string.Empty;
        var kind = evt.Type == EventType.ToolExecuted
            ? "tool"
            : evt.Type == EventType.DecisionMade || string.Equals(category, "react.checkpoint", StringComparison.OrdinalIgnoreCase)
                ? "reasoning"
                : "task";

        var label = evt.Description;
        if (string.Equals(category, "react.checkpoint", StringComparison.OrdinalIgnoreCase)
            && evt.Metadata.TryGetValue("content", out var contentRaw)
            && contentRaw is string contentText)
        {
            label = TryExtractCheckpointLabel(contentText) ?? label;
        }

        var toolName = evt.Metadata.TryGetValue("tool_name", out var toolRaw) ? toolRaw?.ToString() : null;
        var taskId = evt.Metadata.TryGetValue("task_id", out var taskRaw) ? taskRaw?.ToString() : null;

        return new TimelineItem(
            TimestampUtc: evt.Timestamp,
            AgentId: evt.AgentId,
            Kind: kind,
            EventType: evt.Type.ToString(),
            Label: label,
            TaskId: taskId,
            ToolName: toolName,
            Metadata: evt.Metadata);
    }

    private static string? TryExtractCheckpointLabel(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("label", out var labelEl))
            {
                return labelEl.GetString();
            }

            if (doc.RootElement.TryGetProperty("phase", out var phaseEl))
            {
                return phaseEl.GetString();
            }
        }
        catch
        {
            // best-effort for timeline rendering
        }

        return null;
    }
}

internal sealed record TimelineItem(
    DateTime TimestampUtc,
    string AgentId,
    string Kind,
    string EventType,
    string Label,
    string? TaskId,
    string? ToolName,
    Dictionary<string, object> Metadata);