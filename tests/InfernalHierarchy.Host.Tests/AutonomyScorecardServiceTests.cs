using FluentAssertions;
using InfernalHierarchy.Host.Api;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Tools;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AutonomyScorecardServiceTests
{
    [Fact]
    public void GenerateReport_WhenCertificationFailClosedAndNoRuns_ShouldFailCertification()
    {
        var playground = new AgentPlaygroundService();
        var sut = new AutonomyScorecardService(playground);

        var report = sut.GenerateReport(new AutonomyScorecardOptions
        {
            RunsPerScenario = 10,
            CertificationMode = true,
            FailOnInsufficientData = true,
            RequireStructuredOutcomeContract = true,
            MinCoverage = 1.0,
            MinGrade = "B",
            MinSuccessRatePerScenario = 0.8,
        });

        report.CertificationPassed.Should().BeFalse();
        report.Scenarios.Should().OnlyContain(s => s.Status == "insufficient_data");
    }

    [Fact]
    public void GenerateReport_WhenStrictContractAndMissingFields_ShouldFlagContractViolation()
    {
        var playground = new AgentPlaygroundService();
        var sut = new AutonomyScorecardService(playground);

        var scenarioId = playground.CreateScenario(
            name: "simple_search benchmark",
            prompt: "Find a concise answer",
            toAgentId: "lucifer",
            timeoutMs: 10000,
            tags: new Dictionary<string, object>
            {
                ["benchmark_id"] = "simple_search"
            });

        playground.AddRun(
            scenarioId,
            "prompt",
            "lucifer",
            10000,
            new ChatResponse(
                fromAgentId: "lucifer",
                toAgentId: "playground",
                content: "Here is your answer.",
                payload: new Dictionary<string, object>(),
                correlationId: "c1",
                causationId: "c0",
                receivedUtc: DateTime.UtcNow,
                durationMs: 1200));

        var report = sut.GenerateReport(new AutonomyScorecardOptions
        {
            RunsPerScenario = 10,
            CertificationMode = true,
            FailOnInsufficientData = false,
            RequireStructuredOutcomeContract = true,
            MinCoverage = 1.0,
            MinGrade = "B",
            MinSuccessRatePerScenario = 0.8,
        });

        report.CertificationPassed.Should().BeFalse();
        report.ContractViolations.Should().BeGreaterThan(0);
        report.Scenarios.Should().Contain(s => s.Status == "contract_violation");
    }

    [Fact]
    public void GenerateReport_WhenNoRuns_ShouldReturnIncompleteCoverage()
    {
        var playground = new AgentPlaygroundService();
        var sut = new AutonomyScorecardService(playground);

        var report = sut.GenerateReport();

        report.Coverage.Should().Be(0);
        report.Grade.Should().Be("incomplete");
        report.Scenarios.Should().HaveCount(4);
        report.Scenarios.Should().OnlyContain(s => s.Status == "insufficient_data");
    }

    [Fact]
    public void GenerateReport_WhenBenchmarkRunsExist_ShouldComputeScores()
    {
        var playground = new AgentPlaygroundService();
        var sut = new AutonomyScorecardService(playground);

        var scenarioId = playground.CreateScenario(
            name: "simple_search benchmark",
            prompt: "Find a concise answer",
            toAgentId: "lucifer",
            timeoutMs: 10000,
            tags: new Dictionary<string, object>
            {
                ["benchmark_id"] = "simple_search"
            });

        playground.AddRun(
            scenarioId,
            "prompt",
            "lucifer",
            10000,
            new ChatResponse(
                fromAgentId: "lucifer",
                toAgentId: "playground",
                content: "Here is your answer.",
                payload: new Dictionary<string, object>(),
                correlationId: "c1",
                causationId: "c0",
                receivedUtc: DateTime.UtcNow,
                durationMs: 1200));

        playground.AddRun(
            scenarioId,
            "prompt",
            "lucifer",
            10000,
            new ChatResponse(
                fromAgentId: "lucifer",
                toAgentId: "playground",
                content: "Timeout: no report received within 10000ms",
                payload: new Dictionary<string, object>(),
                correlationId: "c2",
                causationId: "c0",
                receivedUtc: DateTime.UtcNow,
                durationMs: 10000));

        var report = sut.GenerateReport(runsPerScenario: 10);

        report.Scenarios.Should().Contain(s => s.ScenarioId == "simple_search" && s.Status == "evaluated");
        report.OverallScore.Should().BeGreaterThan(0);
        report.Recommendations.Should().NotBeEmpty();
    }
}
