using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using InfernalHierarchy.Messaging.Bus;

namespace InfernalHierarchy.Host.Api;

internal static class PerfApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        app.MapGet("/api/perf/snapshot", (HttpContext ctx, PerformanceMonitor perf) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            return Results.Ok(perf.GetCurrentSnapshot());
        });

        app.MapGet("/api/perf/histograms", (HttpContext ctx, MetricsCollector collector) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            return Results.Ok(new
            {
                names = collector.GetHistogramNames(),
                stats = collector.GetAllHistogramStats()
            });
        });

        app.MapGet("/api/perf/http", (HttpContext ctx, MetricsCollector collector) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var all = collector.GetAllHistogramStats();
            var items = all
                .Where(kvp => kvp.Key.StartsWith("http.latency.", StringComparison.OrdinalIgnoreCase)
                              && !kvp.Key.Equals("http.latency.ms", StringComparison.OrdinalIgnoreCase))
                .Select(kvp => new
                {
                    metric = kvp.Key,
                    stats = kvp.Value
                })
                .OrderByDescending(x => x.stats.P95)
                .ThenByDescending(x => x.stats.Count)
                .Take(50)
                .ToList();

            return Results.Ok(new { items });
        });

        app.MapGet("/api/perf/spans", (HttpContext ctx, MetricsCollector collector) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var all = collector.GetAllHistogramStats();
            var items = all
                .Where(kvp => kvp.Key.StartsWith("trace.span.", StringComparison.OrdinalIgnoreCase))
                .Select(kvp => new
                {
                    metric = kvp.Key,
                    stats = kvp.Value
                })
                .OrderByDescending(x => x.stats.P95)
                .ThenByDescending(x => x.stats.Count)
                .Take(50)
                .ToList();

            return Results.Ok(new { items });
        });

        app.MapGet("/api/perf/message-bus", (HttpContext ctx, IMessageBus messageBus) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            if (messageBus is not ChannelMessageBus bus)
            {
                return Results.Ok(new
                {
                    supported = false,
                    reason = "Current IMessageBus implementation does not expose channel diagnostics"
                });
            }

            var targetedDepth = bus.TargetedQueueDepth;
            var broadcastDepth = bus.BroadcastQueueDepth;
            var totalDepth = targetedDepth + broadcastDepth;
            var capacity = Math.Max(1, bus.QueueCapacity);
            var utilization = (double)totalDepth / capacity;

            var recommendations = BuildMessageBusRecommendations(bus, utilization, totalDepth);

            return Results.Ok(new
            {
                supported = true,
                channels = new
                {
                    active = bus.ActiveChannelCount,
                    broadcastSubscribers = bus.ActiveBroadcastSubscriberCount,
                },
                queue = new
                {
                    capacity,
                    overflowPolicy = bus.OverflowPolicy.ToString(),
                    targetedDepth,
                    broadcastDepth,
                    totalDepth,
                    utilization,
                },
                backpressure = new
                {
                    enabled = bus.AdaptiveBackpressureEnabled,
                    active = bus.IsBackpressureActive,
                    deferCollaborationRequests = bus.DeferCollaborationRequests,
                    highWatermark = bus.BackpressureHighWatermark,
                    recoverWatermark = bus.BackpressureRecoverWatermark,
                },
                counters = new
                {
                    droppedMessages = bus.DroppedMessages,
                    rejectedMessages = bus.RejectedMessages,
                    deferredMessages = bus.DeferredMessages,
                },
                recommendations
            });
        });

        app.MapGet("/api/perf/traces", (HttpContext ctx, ITraceCaptureStore store, int? limit) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var items = store.GetRecentTraces(limit ?? 50);
            return Results.Ok(new { items });
        });

        app.MapGet("/api/perf/trace-capture", (HttpContext ctx, ITraceCaptureStore store, IOptions<TraceCaptureOptions> options) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var stats = store.GetStats();
            var o = options.Value;
            return Results.Ok(new
            {
                enabled = o.Enabled,
                maxTraces = o.MaxTraces,
                maxSpansPerTrace = o.MaxSpansPerTrace,
                maxTagsPerSpan = o.MaxTagsPerSpan,
                tracesStored = stats.TraceCount,
                spansStored = stats.SpanCount,
            });
        });

        app.MapGet("/api/perf/request-profiling", (HttpContext ctx, IHttpRequestProfilingStore store, IOptions<PerfRequestProfilingOptions> options) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var o = options.Value;
            var stats = store.GetStats();
            return Results.Ok(new
            {
                enabled = o.Enabled,
                maxRecords = o.MaxRecords,
                retentionMinutes = o.RetentionMinutes,
                includeUiRequests = o.IncludeUiRequests,
                requestsStored = stats.RequestCount,
            });
        });

        app.MapGet("/api/perf/requests", (HttpContext ctx, IHttpRequestProfilingStore store, int? limit) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var items = store.GetRecent(limit ?? 50);
            return Results.Ok(new { items });
        });

        app.MapGet("/api/perf/requests/{id}", (HttpContext ctx, IHttpRequestProfilingStore store, string id) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var item = store.GetById(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        app.MapPost("/api/perf/requests/clear", (HttpContext ctx, IHttpRequestProfilingStore store) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            store.Clear();
            return Results.Ok(new { cleared = true });
        });

        app.MapGet("/api/perf/traces/{traceId}", (HttpContext ctx, ITraceCaptureStore store, string traceId) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var trace = store.GetTrace(traceId);
            return trace is null ? Results.NotFound() : Results.Ok(trace);
        });

        app.MapGet("/api/perf/traces/{traceId}/tree", (HttpContext ctx, ITraceCaptureStore store, string traceId) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var trace = store.GetTrace(traceId);
            if (trace is null)
            {
                return Results.NotFound();
            }

            var start = trace.Summary.StartTimeUtc;

            var nodesById = new Dictionary<string, TraceTreeNodeDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var span in trace.Spans)
            {
                var startOffsetMs = (span.StartTimeUtc - start).TotalMilliseconds;
                if (startOffsetMs < 0) startOffsetMs = 0;

                nodesById[span.SpanId] = new TraceTreeNodeDto
                {
                    SpanId = span.SpanId,
                    ParentSpanId = span.ParentSpanId,
                    Name = span.Name,
                    Kind = span.Kind,
                    Status = span.Status,
                    StartTimeUtc = span.StartTimeUtc,
                    StartOffsetMs = startOffsetMs,
                    DurationMs = span.DurationMs,
                    EndOffsetMs = startOffsetMs + span.DurationMs,
                    Tags = span.Tags,
                };
            }

            var roots = new List<TraceTreeNodeDto>();
            foreach (var node in nodesById.Values)
            {
                if (!string.IsNullOrWhiteSpace(node.ParentSpanId)
                    && nodesById.TryGetValue(node.ParentSpanId, out var parent))
                {
                    parent.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            static void SortChildren(TraceTreeNodeDto node)
            {
                if (node.Children.Count == 0)
                {
                    return;
                }

                node.Children.Sort(static (a, b) => a.StartOffsetMs.CompareTo(b.StartOffsetMs));
                foreach (var child in node.Children)
                {
                    SortChildren(child);
                }
            }

            roots.Sort(static (a, b) => a.StartOffsetMs.CompareTo(b.StartOffsetMs));
            foreach (var root in roots)
            {
                SortChildren(root);
            }

            return Results.Ok(new
            {
                traceId = trace.TraceId,
                summary = trace.Summary,
                roots,
            });
        });

        app.MapGet("/api/perf/traces/{traceId}/download", (HttpContext ctx, ITraceCaptureStore store, string traceId) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var json = store.ExportTraceJson(traceId);
            return Results.Text(json, "application/json; charset=utf-8");
        });

        app.MapPost("/api/perf/traces/clear", (HttpContext ctx, ITraceCaptureStore store) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            store.Clear();
            return Results.Ok(new { cleared = true });
        });
    }

    private static IReadOnlyList<string> BuildMessageBusRecommendations(ChannelMessageBus bus, double utilization, int totalDepth)
    {
        var recommendations = new List<string>();

        if (bus.IsBackpressureActive)
        {
            recommendations.Add("Backpressure is active: prioritize critical tasks and defer collaboration fanout until queue depth recovers.");
        }

        if (utilization >= 0.90d)
        {
            recommendations.Add("Queue utilization is above 90%: increase MessageBus:QueueCapacity or reduce producer burst size.");
        }
        else if (utilization >= 0.75d)
        {
            recommendations.Add("Queue utilization is above 75%: monitor for sustained pressure and consider temporary agent/tool throttling.");
        }

        if (bus.RejectedMessages > 0)
        {
            recommendations.Add("Rejected messages detected: switch overflow policy to Block/DropOldest for bursty workloads or tune admission control.");
        }

        if (bus.DroppedMessages > 0)
        {
            recommendations.Add("Dropped messages detected: validate that lossy behavior is acceptable for the current workload.");
        }

        if (bus.DeferredMessages > 0)
        {
            recommendations.Add("Deferred collaboration requests detected: run collaboration-heavy workflows when queue pressure is lower.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(totalDepth == 0
                ? "Queue is idle: no backpressure action required."
                : "Queue is healthy: continue normal operations.");
        }

        return recommendations;
    }

    private sealed class TraceTreeNodeDto
    {
        public string SpanId { get; init; } = string.Empty;
        public string? ParentSpanId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset StartTimeUtc { get; init; }
        public double StartOffsetMs { get; init; }
        public double DurationMs { get; init; }
        public double EndOffsetMs { get; init; }
        public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
        public List<TraceTreeNodeDto> Children { get; } = new();
    }
}
