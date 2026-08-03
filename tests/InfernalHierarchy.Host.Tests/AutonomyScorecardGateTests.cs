using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Messaging.Bus;
using InfernalHierarchy.Host.Api;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AutonomyScorecardGateTests
{
    [Fact]
    public async Task Evaluate_ShouldFail_WhenRealBenchmarkRunsUnderperform()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var bus = new ChannelMessageBus(NullLogger<ChannelMessageBus>.Instance);
        var playground = new AgentPlaygroundService();
        var sut = new AutonomyScorecardService(playground);

        // Real run path: scenarios are executed over message bus, then scored.
        using var responder = StartBenchmarkResponder(bus, benchmarkId => benchmarkId == "partial_failure_recovery" ? "❌ regression" : "Execution succeeded", cts.Token);
        await ExecuteBenchmarksAsync(playground, bus, runsPerScenario: 10, cts.Token);

        var report = sut.GenerateReport(runsPerScenario: 10);

        var passed = MeetsThresholds(
            report,
            minCoverage: 1.0,
            minGrade: "B",
            minSuccessRatePerScenario: 0.80);

        passed.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluate_ShouldPass_WhenRealBenchmarkRunsMeetReleaseThresholds()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var bus = new ChannelMessageBus(NullLogger<ChannelMessageBus>.Instance);
        var playground = new AgentPlaygroundService();
        var sut = new AutonomyScorecardService(playground);

        using var responder = StartBenchmarkResponder(bus, _ => "Execution succeeded", cts.Token);
        await ExecuteBenchmarksAsync(playground, bus, runsPerScenario: 10, cts.Token);

        var report = sut.GenerateReport(runsPerScenario: 10);

        var passed = MeetsThresholds(
            report,
            minCoverage: 1.0,
            minGrade: "B",
            minSuccessRatePerScenario: 0.80);

        passed.Should().BeTrue();
    }

    private static async Task ExecuteBenchmarksAsync(
        AgentPlaygroundService playground,
        ChannelMessageBus bus,
        int runsPerScenario,
        CancellationToken ct)
    {
        var benchmarks = new[]
        {
            "simple_search",
            "missing_tool_task",
            "multi_step_build",
            "partial_failure_recovery"
        };

        foreach (var benchmarkId in benchmarks)
        {
            var scenarioId = playground.CreateScenario(
                name: $"{benchmarkId} benchmark",
                prompt: $"benchmark:{benchmarkId}",
                toAgentId: "lucifer",
                timeoutMs: 2000,
                tags: new Dictionary<string, object>
                {
                    ["benchmark_id"] = benchmarkId,
                });

            var scenario = playground.GetScenario(scenarioId)!;
            for (var i = 0; i < runsPerScenario; i++)
            {
                var response = await ExecuteScenarioRunAsync(bus, scenario, ct).ConfigureAwait(false);
                playground.AddRun(scenario.ScenarioId, scenario.Prompt, scenario.ToAgentId, scenario.TimeoutMs, response);
            }
        }
    }

    private static async Task<ChatResponse> ExecuteScenarioRunAsync(ChannelMessageBus bus, PlaygroundScenario scenario, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var replyToId = $"bench-{Guid.NewGuid():N}";
        var startedUtc = DateTime.UtcNow;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(scenario.TimeoutMs));
        var enumerator = bus.SubscribeAsync(replyToId, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);

        try
        {
            await bus.PublishAsync(new AgentMessage
            {
                Id = replyToId,
                FromAgentId = replyToId,
                ToAgentId = scenario.ToAgentId,
                Type = MessageType.Task,
                Content = scenario.Prompt,
                CorrelationId = correlationId,
                Payload = new Dictionary<string, object>
                {
                    ["transport"] = "benchmark",
                    ["execution_profile"] = "Research"
                }
            }, ct).ConfigureAwait(false);

            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                var response = enumerator.Current;
                if (response.Type != MessageType.Report)
                {
                    continue;
                }

                return new ChatResponse(
                    fromAgentId: response.FromAgentId,
                    toAgentId: response.ToAgentId,
                    content: response.Content,
                    payload: response.Payload,
                    correlationId: response.CorrelationId ?? correlationId,
                    causationId: response.CausationId,
                    receivedUtc: DateTime.UtcNow,
                    durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds);
            }

            return BuildTimeoutResponse(scenario, correlationId, startedUtc);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return BuildTimeoutResponse(scenario, correlationId, startedUtc);
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            bus.CleanupAgent(replyToId);
        }
    }

    private static ChatResponse BuildTimeoutResponse(PlaygroundScenario scenario, string correlationId, DateTime startedUtc)
    {
        return new ChatResponse(
            fromAgentId: "system",
            toAgentId: scenario.ToAgentId,
            content: $"Timeout: no report received within {scenario.TimeoutMs}ms",
            payload: new Dictionary<string, object>(),
            correlationId: correlationId,
            causationId: null,
            receivedUtc: DateTime.UtcNow,
            durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds);
    }

    private static IDisposable StartBenchmarkResponder(
        ChannelMessageBus bus,
        Func<string, string> contentByBenchmark,
        CancellationToken ct)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var loop = Task.Run(async () =>
        {
            await foreach (var message in bus.SubscribeAsync("lucifer", linkedCts.Token))
            {
                if (message.Type != MessageType.Task)
                {
                    continue;
                }

                var benchmarkId = ExtractBenchmarkId(message.Content);
                var content = contentByBenchmark(benchmarkId);

                await Task.Delay(TimeSpan.FromMilliseconds(25), linkedCts.Token).ConfigureAwait(false);

                await bus.PublishAsync(new AgentMessage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FromAgentId = "lucifer",
                    ToAgentId = message.FromAgentId,
                    Type = MessageType.Report,
                    Content = content,
                    CorrelationId = message.CorrelationId,
                    CausationId = message.Id,
                    Payload = new Dictionary<string, object>
                    {
                        ["benchmark_id"] = benchmarkId
                    }
                }, linkedCts.Token).ConfigureAwait(false);
            }
        }, linkedCts.Token);

        return new AsyncDisposableAction(async () =>
        {
            linkedCts.Cancel();
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                bus.CleanupAgent("lucifer");
                linkedCts.Dispose();
            }
        });
    }

    private static string ExtractBenchmarkId(string prompt)
    {
        const string marker = "benchmark:";
        var idx = prompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return "unknown";
        }

        return prompt[(idx + marker.Length)..].Trim();
    }

    private static bool MeetsThresholds(
        AutonomyScorecardReport report,
        double minCoverage,
        string minGrade,
        double minSuccessRatePerScenario)
    {
        if (report.Coverage < minCoverage)
        {
            return false;
        }

        if (!IsGradeAtLeast(report.Grade, minGrade))
        {
            return false;
        }

        foreach (var scenario in report.Scenarios)
        {
            if (scenario.Status != "evaluated")
            {
                return false;
            }

            if (scenario.SuccessRate < minSuccessRatePerScenario)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsGradeAtLeast(string actual, string minimum)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 5,
            ["B"] = 4,
            ["C"] = 3,
            ["D"] = 2,
            ["E"] = 1,
            ["incomplete"] = 0,
        };

        return order.TryGetValue(actual, out var actualRank)
            && order.TryGetValue(minimum, out var minimumRank)
            && actualRank >= minimumRank;
    }

    private sealed class AsyncDisposableAction : IDisposable
    {
        private readonly Func<Task> _disposeAsync;
        private int _disposed;

        public AsyncDisposableAction(Func<Task> disposeAsync)
        {
            _disposeAsync = disposeAsync;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _disposeAsync().GetAwaiter().GetResult();
        }
    }
}
