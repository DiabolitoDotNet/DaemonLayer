using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace InfernalHierarchy.Core.Eventing;

/// <summary>
/// Event sourcing store for complete audit trail of all agent actions
/// </summary>
public sealed class EventStore : IAgentEventSink, IDisposable
{
    private readonly string _storePath;
    private readonly ILogger<EventStore> _logger;
    private readonly ConcurrentQueue<AgentEvent> _eventQueue = new();
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private readonly PeriodicTimer _flushTimer;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public EventStore(string storePath, ILogger<EventStore> logger)
    {
        _storePath = storePath;
        _logger = logger;
        _flushTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        Directory.CreateDirectory(_storePath);

        _ = Task.Run(FlushEventsLoop);

        _logger.LogInformation("📜 Event store initialized at: {Path}", _storePath);
    }

    /// <summary>
    /// Append an event to the store
    /// </summary>
    public void AppendEvent(AgentEvent evt)
    {
        _eventQueue.Enqueue(evt);
    }

    /// <summary>
    /// Get all events for a specific agent
    /// </summary>
    public async Task<IEnumerable<AgentEvent>> GetAgentEventsAsync(string agentId, CancellationToken ct = default)
    {
        var events = new List<AgentEvent>();
        var eventFile = GetEventFilePath(agentId);

        if (!File.Exists(eventFile))
        {
            return events;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(eventFile, ct);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var evt = JsonSerializer.Deserialize<AgentEvent>(line);
                if (evt != null)
                {
                    events.Add(evt);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read events for agent {AgentId}", agentId);
        }

        return events;
    }

    /// <summary>
    /// Replay all events to reconstruct agent state
    /// </summary>
    public async Task<AgentState> ReplayEventsAsync(string agentId, CancellationToken ct = default)
    {
        var events = await GetAgentEventsAsync(agentId, ct);
        var state = new AgentState { AgentId = agentId };

        foreach (var evt in events.OrderBy(e => e.Timestamp))
        {
            ApplyEvent(state, evt);
        }

        return state;
    }

    /// <summary>
    /// Get events within a time range
    /// </summary>
    public async Task<IEnumerable<AgentEvent>> GetEventsByTimeRangeAsync(
        DateTime startTime,
        DateTime endTime,
        CancellationToken ct = default)
    {
        var events = new List<AgentEvent>();
        var eventFiles = Directory.GetFiles(_storePath, "events_*.jsonl");

        foreach (var file in eventFiles)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var evt = JsonSerializer.Deserialize<AgentEvent>(line);
                    if (evt != null && evt.Timestamp >= startTime && evt.Timestamp <= endTime)
                    {
                        events.Add(evt);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read event file {File}", file);
            }
        }

        return events.OrderBy(e => e.Timestamp);
    }

    private async Task FlushEventsLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await _flushTimer.WaitForNextTickAsync(_cts.Token);
                await FlushEventsAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing events");
            }
        }
    }

    private async Task FlushEventsAsync()
    {
        if (_eventQueue.IsEmpty) return;

        await _writeSemaphore.WaitAsync();
        try
        {
            var eventsByAgent = new Dictionary<string, List<AgentEvent>>();

            while (_eventQueue.TryDequeue(out var evt))
            {
                if (!eventsByAgent.ContainsKey(evt.AgentId))
                {
                    eventsByAgent[evt.AgentId] = new List<AgentEvent>();
                }
                eventsByAgent[evt.AgentId].Add(evt);
            }

            foreach (var (agentId, events) in eventsByAgent)
            {
                var eventFile = GetEventFilePath(agentId);
                var lines = events.Select(e => JsonSerializer.Serialize(e));

                await File.AppendAllLinesAsync(eventFile, lines);
            }

            _logger.LogDebug("Flushed {Count} events to disk", eventsByAgent.Sum(kvp => kvp.Value.Count));
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    private void ApplyEvent(AgentState state, AgentEvent evt)
    {
        state.EventCount++;
        state.LastEventTimestamp = evt.Timestamp;

        switch (evt.Type)
        {
            case EventType.AgentCreated:
                state.Created = evt.Timestamp;
                break;
            case EventType.TaskReceived:
                state.TasksReceived++;
                break;
            case EventType.TaskCompleted:
                state.TasksCompleted++;
                break;
            case EventType.ToolExecuted:
                state.ToolExecutions++;
                break;
            case EventType.DecisionMade:
                state.DecisionsMade++;
                break;
            case EventType.AgentTerminated:
                state.Terminated = evt.Timestamp;
                break;
        }
    }

    private string GetEventFilePath(string agentId)
    {
        // Sanitize agent ID for filename
        var safeAgentId = string.Join("_", agentId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storePath, $"events_{safeAgentId}.jsonl");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        try
        {
            _flushTimer.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        // Final flush (best effort)
        try
        {
            FlushEventsAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // best-effort
        }

        try
        {
            _writeSemaphore.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        try
        {
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
    }
}

public class AgentEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string AgentId { get; set; } = string.Empty;
    public EventType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public enum EventType
{
    AgentCreated,
    AgentTerminated,
    TaskReceived,
    TaskStarted,
    TaskCompleted,
    TaskFailed,
    ToolExecuted,
    DecisionMade,
    MemoryWritten,
    MemoryRead,
    MessageSent,
    MessageReceived,
    ErrorOccurred
}

public class AgentState
{
    public string AgentId { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime? Terminated { get; set; }
    public int TasksReceived { get; set; }
    public int TasksCompleted { get; set; }
    public int ToolExecutions { get; set; }
    public int DecisionsMade { get; set; }
    public int EventCount { get; set; }
    public DateTime LastEventTimestamp { get; set; }
}
