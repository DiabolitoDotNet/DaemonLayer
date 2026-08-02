using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Learning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class DefaultToolExecutionPipelineTests
{
    private sealed class InMemoryFailedOperationStoreForTests : IFailedOperationStore
    {
        public List<FailedOperationRecord> Records { get; } = new();

        public Task RecordAsync(FailedOperationRecord record, CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FailedOperationRecord>> GetRecentAsync(int limit, bool pendingOnly, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedOperationRecord>>(Records);

        public Task<FailedOperationRecord?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult<FailedOperationRecord?>(Records.FirstOrDefault(r => r.Id == id));

        public Task<FailedOperationRecord?> TryStartReplayAsync(string id, string requestedBy, CancellationToken ct = default)
            => Task.FromResult<FailedOperationRecord?>(null);

        public Task MarkReplaySucceededAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkReplayFailedAsync(string id, string reasonCode, string? error, CancellationToken ct = default) => Task.CompletedTask;

        public FailedOperationStats GetStats() => new(Records.Count, Records.Count, 0, 0);
    }

    private sealed class DenyAllAuthorizationService : IToolAuthorizationService
    {
        public AuthorizationResult IsAuthorized(string agentId, string agentName, AgentRank rank, string toolName)
            => AuthorizationResult.Failure("denied by test");

        public List<string> GetAuthorizedTools(string agentId, string agentName, AgentRank rank) => new();

        public void ReloadPermissions() { }
    }

    private sealed class FakeTool : ITool
    {
        private readonly Func<Dictionary<string, object>, CancellationToken, Task<ToolResult>> _handler;

        public FakeTool(string name, string description, Func<Dictionary<string, object>, CancellationToken, Task<ToolResult>> handler)
        {
            Name = name;
            Description = description;
            _handler = handler;
        }

        public string Name { get; }
        public string Description { get; }

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default) => _handler(parameters, ct);
    }

    private sealed class CapturingEventSink : IAgentEventSink
    {
        public readonly List<AgentEvent> Events = new();

        public void AppendEvent(AgentEvent evt) => Events.Add(evt);
    }

    private sealed class ThrowingEventSink : IAgentEventSink
    {
        public void AppendEvent(AgentEvent evt) => throw new InvalidOperationException("append failed");
    }

    private sealed class ConcurrencyGateLimiter : IToolExecutionLimiter
    {
        private readonly SemaphoreSlim _semaphore;

        public ConcurrencyGateLimiter(int maxConcurrency)
        {
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                return await operation(ct);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    private sealed class TimeoutLimiter : IToolExecutionLimiter
    {
        private readonly TimeSpan _timeout;

        public TimeoutLimiter(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(_timeout);

            try
            {
                return await operation(budget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.IsCancellationRequested)
            {
                throw new TimeoutException("Tool execution exceeded test timeout budget.");
            }
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

        private readonly List<Entry> _entries = new();
        public IReadOnlyList<Entry> Entries => _entries;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_Success_AppendsToolExecutedEvent_AndRecordsLearning()
    {
        var learning = new AgentLearningService(NullLogger<AgentLearningService>.Instance);
        var sink = new CapturingEventSink();

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: learning,
            exceptionHandler: null,
            eventSink: sink);

        var tool = new FakeTool("t1", "d", (p, ct) => Task.FromResult(new ToolResult { Success = true, Output = "ok" }));

        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object> { ["x"] = 1 },
            AgentId: "agent-1",
            AgentRank: "Duke",
            CancellationToken: CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Single(sink.Events);
        Assert.Equal(EventType.ToolExecuted, sink.Events[0].Type);
        Assert.Equal("agent-1", sink.Events[0].AgentId);
        Assert.Equal("t1", (string)sink.Events[0].Metadata["tool"]);

        Assert.Equal(1.0, learning.GetToolSuccessRate("t1"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAuthorizationDenies_ReturnsFailure_AndDoesNotExecuteTool()
    {
        var auth = new DenyAllAuthorizationService();

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: null,
            eventSink: null,
            rateLimiter: null,
            authorizationService: auth);

        var toolWasCalled = false;
        var tool = new FakeTool("t-denied", "d", (p, ct) =>
        {
            toolWasCalled = true;
            return Task.FromResult(new ToolResult { Success = true, Output = "ok" });
        });

        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object>(),
            AgentId: "agent-1",
            AgentRank: "Duke",
            CancellationToken: CancellationToken.None,
            AgentName: "Baal");

        var result = await pipeline.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.False(toolWasCalled);
        Assert.Contains("Access denied", result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenToolThrows_ReturnsFailure_AppendsErrorEvent_AndRecordsLearning()
    {
        var learning = new AgentLearningService(NullLogger<AgentLearningService>.Instance);
        var sink = new CapturingEventSink();

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: learning,
            exceptionHandler: null,
            eventSink: sink);

        var tool = new FakeTool("t2", "d", (p, ct) => throw new InvalidOperationException("boom"));

        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object> { ["x"] = 1 },
            AgentId: "agent-2",
            AgentRank: null,
            CancellationToken: CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Contains("boom", result.Error ?? string.Empty, StringComparison.Ordinal);

        Assert.Single(sink.Events);
        Assert.Equal(EventType.ErrorOccurred, sink.Events[0].Type);
        Assert.Equal("t2", (string)sink.Events[0].Metadata["tool"]);
        Assert.True((bool)sink.Events[0].Metadata["success"] == false);
        Assert.True(sink.Events[0].Metadata.ContainsKey("error"));

        Assert.Equal(0.0, learning.GetToolSuccessRate("t2"));
    }

    [Fact]
    public async Task ExecuteAsync_SafeSerialize_WhenJsonSerializationFails_FallsBackToKeyValuePairs()
    {
        var sink = new CapturingEventSink();

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: null,
            eventSink: sink);

        var tool = new FakeTool("t3", "d", (p, ct) => Task.FromResult(new ToolResult { Success = true, Output = "ok" }));

        var parameters = new Dictionary<string, object>
        {
            // Delegates are not supported by System.Text.Json; this should force SafeSerialize fallback.
            ["callback"] = (Action)(() => { })
        };

        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: parameters,
            AgentId: "agent-3",
            AgentRank: "Worker",
            CancellationToken: CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Single(sink.Events);

        var json = (string)sink.Events[0].Metadata["parameters_json"];
        Assert.Contains("callback=", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEventSinkThrows_DoesNotFailToolExecution_AndLogsDebug()
    {
        var logger = new ListLogger<DefaultToolExecutionPipeline>();
        var sink = new ThrowingEventSink();

        var pipeline = new DefaultToolExecutionPipeline(
            logger,
            learningService: null,
            exceptionHandler: null,
            eventSink: sink);

        var tool = new FakeTool("t4", "d", (p, ct) => Task.FromResult(new ToolResult { Success = true, Output = "ok" }));

        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object> { ["x"] = 1 },
            AgentId: "agent-4",
            AgentRank: "Worker",
            CancellationToken: CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("Failed to append tool event", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithExceptionHandler_UsesHandlerOnSuccessPath()
    {
        var sink = new CapturingEventSink();
        var exceptionHandler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: exceptionHandler,
            eventSink: sink);

        var tool = new FakeTool("t5", "d", (p, ct) => Task.FromResult(new ToolResult { Success = true, Output = "ok" }));

        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object> { ["x"] = 1 },
            AgentId: "agent-5",
            AgentRank: "Worker",
            CancellationToken: CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Single(sink.Events);
        Assert.Equal(EventType.ToolExecuted, sink.Events[0].Type);
    }

    [Fact]
    public async Task ExecuteAsync_WithExceptionHandler_WhenToolThrows_ReturnsFailureAndAppendsErrorEvent()
    {
        var sink = new CapturingEventSink();
        var exceptionHandler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: exceptionHandler,
            eventSink: sink);

        var tool = new FakeTool("t6", "d", (p, ct) => throw new InvalidOperationException("boom"));

        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object> { ["x"] = 1 },
            AgentId: "agent-6",
            AgentRank: "Worker",
            CancellationToken: CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Single(sink.Events);
        Assert.Equal(EventType.ErrorOccurred, sink.Events[0].Type);
    }

    [Fact]
    public async Task ExecuteAsync_WithExecutionLimiterTimeout_ReturnsExplicitTimeoutFailure()
    {
        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            executionLimiter: new TimeoutLimiter(TimeSpan.FromMilliseconds(25)));

        var tool = new FakeTool("slow", "d", async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            return new ToolResult { Success = true, Output = "ok" };
        });

        var result = await pipeline.ExecuteAsync(new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object>(),
            AgentId: "agent-7",
            AgentRank: "Worker",
            CancellationToken: CancellationToken.None));

        Assert.False(result.Success);
        Assert.Contains("timeout", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Metadata.TryGetValue("resource_limit_timeout", out var timeoutFlag));
        Assert.True(timeoutFlag is true);
    }

    [Fact]
    public async Task ExecuteAsync_WithExecutionLimiterStress_ShouldBoundConcurrencyUnderLoad()
    {
        var limiter = new ConcurrencyGateLimiter(maxConcurrency: 2);
        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            executionLimiter: limiter);

        var current = 0;
        var observedMax = 0;

        var tool = new FakeTool("bounded", "d", async (_, ct) =>
        {
            var now = Interlocked.Increment(ref current);
            while (true)
            {
                var maxSeen = Volatile.Read(ref observedMax);
                if (now <= maxSeen)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref observedMax, now, maxSeen) == maxSeen)
                {
                    break;
                }
            }

            await Task.Delay(40, ct);
            Interlocked.Decrement(ref current);
            return new ToolResult { Success = true, Output = "ok" };
        });

        var runs = Enumerable.Range(0, 24).Select(_ =>
            pipeline.ExecuteAsync(new ToolExecutionContext(
                ToolName: tool.Name,
                Tool: tool,
                Parameters: new Dictionary<string, object>(),
                AgentId: "agent-8",
                AgentRank: "Worker",
                CancellationToken: CancellationToken.None)));

        var results = await Task.WhenAll(runs);

        Assert.All(results, r => Assert.True(r.Success));
        Assert.True(observedMax <= 2, $"Observed max concurrency {observedMax} exceeded limiter bound.");
    }

    [Fact]
    public async Task ExecuteAsync_WhenToolReturnsFailure_RecordsDeadLetterEntry()
    {
        var failedStore = new InMemoryFailedOperationStoreForTests();

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            failedOperationStore: failedStore);

        var tool = new FakeTool("failing_tool", "d", (_, _) =>
            Task.FromResult(new ToolResult
            {
                Success = false,
                Error = "boom",
                Output = string.Empty
            }));

        var result = await pipeline.ExecuteAsync(new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object> { ["query"] = "x" },
            AgentId: "agent-9",
            AgentRank: "Worker",
            CancellationToken: CancellationToken.None));

        Assert.False(result.Success);
        Assert.Single(failedStore.Records);
        Assert.Equal(FailedOperationKind.ToolExecution, failedStore.Records[0].Kind);
        Assert.Equal("tool_result_failed", failedStore.Records[0].ReasonCode);
        Assert.Equal("failing_tool", failedStore.Records[0].OperationName);
        Assert.False(string.IsNullOrWhiteSpace(failedStore.Records[0].PayloadJson));
    }
}
