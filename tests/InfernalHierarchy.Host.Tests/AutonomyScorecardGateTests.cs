using FluentAssertions;
using InfernalHierarchy.Host.Api;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Tools;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AutonomyScorecardGateTests
{
    [Fact]
    public void Evaluate_ShouldFail_WhenCoverageOrGradeIsBelowThreshold()
    {
        var playground = new AgentPlaygroundService();
        var sut = new AutonomyScorecardService(playground);

        var report = sut.GenerateReport(runsPerScenario: 10);

        var passed = MeetsThresholds(
            report,
            minCoverage: 1.0,
            minGrade: "B",
            minSuccessRatePerScenario: 0.80);

        passed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ShouldPass_WhenAllBenchmarksMeetReleaseThresholds()
    {
        var playground = new AgentPlaygroundService();
        var sut = new AutonomyScorecardService(playground);

        SeedScenario(playground, "simple_search", "simple_search benchmark", durationMs: 1200, runs: 10, includeOneTimeout: false);
        SeedScenario(playground, "missing_tool_task", "missing_tool_task benchmark", durationMs: 2200, runs: 10, includeOneTimeout: false);
        SeedScenario(playground, "multi_step_build", "multi_step_build benchmark", durationMs: 3800, runs: 10, includeOneTimeout: false);
        SeedScenario(playground, "partial_failure_recovery", "partial_failure_recovery benchmark", durationMs: 2600, runs: 10, includeOneTimeout: false);

        var report = sut.GenerateReport(runsPerScenario: 10);

        var passed = MeetsThresholds(
            report,
            minCoverage: 1.0,
            minGrade: "B",
            minSuccessRatePerScenario: 0.80);

        passed.Should().BeTrue();
    }

    private static void SeedScenario(
        AgentPlaygroundService playground,
        string benchmarkId,
        string name,
        int durationMs,
        int runs,
        bool includeOneTimeout)
    {
        var scenarioId = playground.CreateScenario(
            name: name,
            prompt: "benchmark prompt",
            toAgentId: "lucifer",
            timeoutMs: 10_000,
            tags: new Dictionary<string, object>
            {
                ["benchmark_id"] = benchmarkId,
            });

        for (var i = 0; i < runs; i++)
        {
            var timeout = includeOneTimeout && i == runs - 1;
            playground.AddRun(
                scenarioId,
                "prompt",
                "lucifer",
                10_000,
                new ChatResponse(
                    fromAgentId: "lucifer",
                    toAgentId: "playground",
                    content: timeout ? "Timeout: no report received" : "Execution succeeded",
                    payload: new Dictionary<string, object>(),
                    correlationId: $"c-{benchmarkId}-{i}",
                    causationId: "c0",
                    receivedUtc: DateTime.UtcNow,
                    durationMs: timeout ? 10_000 : durationMs));
        }
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
}
