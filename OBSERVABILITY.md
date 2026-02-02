# Observability Integration Guide

## Overview
InfernalHierarchy now includes comprehensive observability features:
- **Distributed Tracing** via OpenTelemetry (compatible with Jaeger, Zipkin, etc.)
- **Performance Monitoring** with CPU and memory metrics
- **Health Checks** for all external dependencies
- **Structured Logging** with enriched context

## Distributed Tracing

### Setup
The system automatically exports traces to the console. To export to external systems:

**Jaeger (recommended for local development):**
```bash
# Run Jaeger all-in-one
docker run -d --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  jaegertracing/all-in-one:latest

# Access UI at http://localhost:16686
```

**Update Program.cs to export to Jaeger:**
```csharp
.WithTracing(tracing => tracing
    .AddSource("InfernalHierarchy")
    .AddHttpClientInstrumentation()
    .AddConsoleExporter()
    .AddOtlpExporter(options => options.Endpoint = new Uri("http://localhost:4317")))
```

### Using Distributed Tracing in Code

**In Agent Classes:**
```csharp
public class MyCustomAgent : BaseAgent
{
    private readonly DistributedTracing _tracing;

    public MyCustomAgent(..., DistributedTracing tracing)
    {
        _tracing = tracing;
    }

    protected override async Task<AgentMessage> ProcessTaskAsync(AgentMessage message, CancellationToken ct)
    {
        using var activity = _tracing.StartAgentActivity(Name, Id, "ProcessTask");
        
        try
        {
            activity?.AddTag("task.complexity", "high");
            activity?.AddEvent("TaskStarted", new Dictionary<string, object?> 
            { 
                ["message_id"] = message.Id 
            });

            // Your processing logic here...
            var result = await DoWork(message, ct);

            activity?.RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            _tracing.RecordError(activity, ex);
            throw;
        }
    }
}
```

**In Tool Implementations:**
```csharp
public class MyCustomTool : ITool
{
    private readonly DistributedTracing _tracing;

    public MyCustomTool(DistributedTracing tracing)
    {
        _tracing = tracing;
    }

    public async Task<ToolResult> ExecuteAsync(string input, string agentId, CancellationToken ct)
    {
        using var activity = _tracing.StartToolActivity(Name, agentId);
        
        try
        {
            activity?.AddTag("input.length", input.Length);
            
            // Tool execution logic
            var result = await PerformWork(input, ct);
            
            activity?.RecordMetric("result.count", result.Items.Count);
            activity?.RecordSuccess();
            
            return new ToolResult { Success = true, Output = result };
        }
        catch (Exception ex)
        {
            _tracing.RecordError(activity, ex);
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}
```

**For LLM Calls:**
```csharp
public async Task<string> CallLlmAsync(string prompt, CancellationToken ct)
{
    using var activity = _tracing.StartLlmActivity("llama3.2", agentId);
    
    try
    {
        activity?.AddTag("prompt.length", prompt.Length);
        
        var startTime = DateTime.UtcNow;
        var response = await _ollamaClient.CompleteChatAsync(messages, ct);
        var duration = DateTime.UtcNow - startTime;
        
        activity?.RecordDuration("llm.call", duration);
        activity?.AddTag("response.length", response.Length);
        activity?.RecordSuccess();
        
        return response;
    }
    catch (Exception ex)
    {
        _tracing.RecordError(activity, ex);
        throw;
    }
}
```

**For Memory Operations:**
```csharp
public async Task WriteToMemoryAsync(MemoryEntry entry, CancellationToken ct)
{
    using var activity = _tracing.StartMemoryActivity("Write", entry.Type);
    
    try
    {
        activity?.AddTag("entry.key", entry.Key);
        
        await _sharedMemory.WriteAsync(entry, ct);
        
        activity?.RecordSuccess();
    }
    catch (Exception ex)
    {
        _tracing.RecordError(activity, ex);
        throw;
    }
}
```

### Trace Context Propagation
Traces automatically propagate through:
- Message bus communication (via Activity.Current)
- HTTP requests (via HttpClientInstrumentation)
- Async/await continuations

## Performance Monitoring

### Metrics Available
The `PerformanceMonitor` automatically collects:
- **Memory:**
  - `system.memory.working_set.mb` - Physical memory used
  - `system.memory.private.mb` - Private working set
  - `system.memory.gc.mb` - Managed heap size
- **CPU:**
  - `system.cpu.usage.percent` - Process CPU usage
- **Threads:**
  - `system.threads.count` - Active thread count
- **Garbage Collection:**
  - `system.gc.gen0_collections` - Gen 0 collections
  - `system.gc.gen1_collections` - Gen 1 collections
  - `system.gc.gen2_collections` - Gen 2 collections
- **System:**
  - `system.handles.count` - OS handle count

### Accessing Performance Snapshots
```csharp
public class MyService
{
    private readonly PerformanceMonitor _perfMonitor;

    public async Task CheckHealthAsync()
    {
        var snapshot = _perfMonitor.GetCurrentSnapshot();
        
        if (snapshot.WorkingSetMB > 1024) // 1GB threshold
        {
            _logger.LogWarning("High memory usage: {Memory:F2}MB", snapshot.WorkingSetMB);
        }
        
        if (snapshot.CpuUsagePercent > 80)
        {
            _logger.LogWarning("High CPU usage: {Cpu:F2}%", snapshot.CpuUsagePercent);
        }
    }
}
```

### Metrics Service Integration
The `MetricsService` tracks application-level metrics:
```csharp
// In agent creation
_metricsService.RecordAgentCreated(rank);

// In message handling
_metricsService.RecordMessageSent(messageType);

// In tool execution
using var _ = _metricsService.TrackToolExecution(toolName);

// In LLM calls
using var __ = _metricsService.TrackLlmCall(model);
```

## Health Checks

### Accessing Health Status
Health checks run automatically. To query them programmatically:

```csharp
public class MonitoringService
{
    private readonly HealthCheckService _healthService;

    public async Task<HealthReport> GetHealthAsync()
    {
        return await _healthService.CheckHealthAsync();
    }

    public async Task<bool> IsSystemHealthyAsync()
    {
        var report = await _healthService.CheckHealthAsync();
        return report.Status == HealthStatus.Healthy;
    }
}
```

### Health Check Details
- **OllamaHealthCheck**: Verifies LLM connectivity
- **TelegramHealthCheck**: Confirms bot can receive updates
- **LiteDbHealthCheck**: Tests database read/write
- **AgentHierarchyHealthCheck**: Validates agent counts and hierarchy

### Exposing Health Checks via HTTP (Optional)
If you add ASP.NET Core health check endpoints:
```csharp
// In Program.cs (requires ASP.NET Core package)
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

## Troubleshooting

### High Memory Usage
1. Check GC collection counts: `system.gc.gen2_collections`
2. Review agent count: `agents.total.count`
3. Check memory operations: `memory.write.count`

### High CPU Usage
1. Review LLM call frequency: `llm.calls.count`
2. Check tool execution counts: `tool.executions.count`
3. Verify no infinite loops in ReAct loops

### Missing Traces
1. Ensure OpenTelemetry exporter is configured
2. Check Activity.Current is not null
3. Verify `DistributedTracing` is injected via DI

### Performance Impact
- Tracing overhead: ~1-3% CPU
- Metrics collection: ~0.5% CPU (updates every 30s)
- Health checks: Minimal (only on demand)

## Best Practices

1. **Always use `using` statements** with activities for automatic disposal
2. **Add meaningful tags** to activities for filtering in trace viewers
3. **Record errors** consistently with `_tracing.RecordError(activity, ex)`
4. **Use correlation IDs** from MessageContextEnricher for log-trace correlation
5. **Monitor memory trends** over time, not just current values
6. **Set alerts** on P95 latency for critical operations
7. **Use sampling** in production (50% sample rate) to reduce overhead

## Configuration

### appsettings.json
```json
{
  "OpenTelemetry": {
    "ServiceName": "InfernalHierarchy",
    "ServiceVersion": "1.0.0",
    "Exporters": {
      "Otlp": {
        "Endpoint": "http://localhost:4317",
        "Protocol": "grpc"
      }
    },
    "Sampling": {
      "Type": "ParentBased",
      "Probability": 1.0
    }
  }
}
```

### Production Recommendations
- Use OTLP exporter to Jaeger/Tempo/Zipkin
- Enable sampling (0.1 = 10% of traces)
- Set up retention policies for traces (7-30 days)
- Configure alerts on health check failures
- Monitor memory and CPU thresholds
