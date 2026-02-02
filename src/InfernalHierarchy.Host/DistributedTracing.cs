using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host;

/// <summary>
/// Distributed tracing support with Activity (OpenTelemetry compatible)
/// </summary>
public class DistributedTracing
{
    public static readonly ActivitySource ActivitySource = new("InfernalHierarchy", "1.0.0");

    private readonly ILogger<DistributedTracing> _logger;
    private readonly MessageContextEnricher _messageEnricher;

    public DistributedTracing(ILogger<DistributedTracing> logger, MessageContextEnricher messageEnricher)
    {
        _logger = logger;
        _messageEnricher = messageEnricher;
    }

    /// <summary>
    /// Start a new activity for agent processing
    /// </summary>
    public Activity? StartAgentActivity(string agentName, string agentId, string operationType)
    {
        var activity = ActivitySource.StartActivity($"Agent.{operationType}", ActivityKind.Internal);
        activity?.SetTag("agent.name", agentName);
        activity?.SetTag("agent.id", agentId);
        activity?.SetTag("operation.type", operationType);

        return activity;
    }

    /// <summary>
    /// Start a new activity for message processing
    /// </summary>
    public Activity? StartMessageActivity(string messageId, string messageType, string fromAgentId, string? toAgentId)
    {
        var activity = ActivitySource.StartActivity($"Message.{messageType}", ActivityKind.Internal);
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.type", messageType);
        activity?.SetTag("message.from", fromAgentId);
        activity?.SetTag("message.to", toAgentId ?? "broadcast");

        // Set correlation ID in log context
        if (activity != null)
        {
            _messageEnricher.CorrelationId = activity.TraceId.ToString();
            _messageEnricher.MessageId = messageId;
            _messageEnricher.MessageType = messageType;
        }

        return activity;
    }

    /// <summary>
    /// Start a new activity for tool execution
    /// </summary>
    public Activity? StartToolActivity(string toolName, string agentId)
    {
        var activity = ActivitySource.StartActivity($"Tool.{toolName}", ActivityKind.Internal);
        activity?.SetTag("tool.name", toolName);
        activity?.SetTag("agent.id", agentId);

        return activity;
    }

    /// <summary>
    /// Start a new activity for LLM call
    /// </summary>
    public Activity? StartLlmActivity(string model, string agentId)
    {
        var activity = ActivitySource.StartActivity("LLM.Call", ActivityKind.Client);
        activity?.SetTag("llm.model", model);
        activity?.SetTag("llm.provider", "ollama");
        activity?.SetTag("agent.id", agentId);

        return activity;
    }

    /// <summary>
    /// Start a new activity for memory operation
    /// </summary>
    public Activity? StartMemoryActivity(string operationType, string entryType)
    {
        var activity = ActivitySource.StartActivity($"Memory.{operationType}", ActivityKind.Internal);
        activity?.SetTag("memory.operation", operationType);
        activity?.SetTag("memory.entry_type", entryType);

        return activity;
    }

    /// <summary>
    /// Record an error in the current activity
    /// </summary>
    public void RecordError(Activity? activity, Exception exception)
    {
        if (activity == null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().Name);
        activity.SetTag("error.message", exception.Message);
        activity.SetTag("error.stacktrace", exception.StackTrace);

        _logger.LogError(exception, "Error recorded in activity {ActivityId}", activity.Id);
    }

    /// <summary>
    /// Add custom tag to current activity
    /// </summary>
    public void AddTag(Activity? activity, string key, object? value)
    {
        activity?.SetTag(key, value);
    }

    /// <summary>
    /// Add event to current activity
    /// </summary>
    public void AddEvent(Activity? activity, string eventName, Dictionary<string, object?>? tags = null)
    {
        if (activity == null) return;

        // Create ActivityEvent with tags using ActivityTagsCollection
        var tagsCollection = tags != null
            ? new ActivityTagsCollection(tags.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value)))
            : null;

        var activityEvent = tagsCollection != null
            ? new ActivityEvent(eventName, tags: tagsCollection)
            : new ActivityEvent(eventName);

        activity.AddEvent(activityEvent);
    }
}

/// <summary>
/// Extensions for easy activity usage
/// </summary>
public static class ActivityExtensions
{
    public static void RecordSuccess(this Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    public static void RecordMetric(this Activity? activity, string name, double value)
    {
        activity?.SetTag($"metric.{name}", value);
    }

    public static void RecordDuration(this Activity? activity, string operation, TimeSpan duration)
    {
        activity?.SetTag($"duration.{operation}.ms", duration.TotalMilliseconds);
    }
}

/// <summary>
/// Scoped activity helper for automatic disposal
/// </summary>
public class ActivityScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly DistributedTracing _tracing;

    public ActivityScope(Activity? activity, DistributedTracing tracing)
    {
        _activity = activity;
        _tracing = tracing;
    }

    public Activity? Activity => _activity;

    public void RecordSuccess()
    {
        _activity?.SetStatus(ActivityStatusCode.Ok);
    }

    public void RecordError(Exception exception)
    {
        _tracing.RecordError(_activity, exception);
    }

    public void AddTag(string key, object? value)
    {
        _tracing.AddTag(_activity, key, value);
    }

    public void Dispose()
    {
        _activity?.Dispose();
    }
}
