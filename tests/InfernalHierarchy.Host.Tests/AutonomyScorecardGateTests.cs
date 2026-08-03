using FluentAssertions;
using InfernalHierarchy.Host.Api;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Tests.E2E;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

[Collection("Host E2E")]
public sealed class AutonomyScorecardGateTests
{
    [Fact]
    public async Task Evaluate_ShouldFail_WhenRealAgentRunsUnderperform()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        ConfigureOperatorHeader(factory, client);

        var runCount = 3;
        await SeedScenarioRunsAsync(client, "simple_search", prompt: "Say hello", timeoutMs: 12000, runCount, CancellationToken.None);
        await SeedScenarioRunsAsync(client, "missing_tool_task", prompt: "Say hello", timeoutMs: 12000, runCount, CancellationToken.None);
        await SeedScenarioRunsAsync(client, "multi_step_build", prompt: "Say hello", timeoutMs: 15000, runCount, CancellationToken.None);

        // Force underperformance with impossible timeout.
        await SeedScenarioRunsAsync(client, "partial_failure_recovery", prompt: "Say hello", timeoutMs: 1, runCount, CancellationToken.None);

        var sut = factory.Services.GetRequiredService<AutonomyScorecardService>();
        var report = sut.GenerateReport(runsPerScenario: runCount);

        var passed = MeetsThresholds(
            report,
            minCoverage: 1.0,
            minGrade: "B",
            minSuccessRatePerScenario: 0.80);

        passed.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluate_ShouldPass_WhenRealAgentRunsMeetReleaseThresholds()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        ConfigureOperatorHeader(factory, client);

        var runCount = 3;
        await SeedScenarioRunsAsync(client, "simple_search", prompt: "Say hello", timeoutMs: 12000, runCount, CancellationToken.None);
        await SeedScenarioRunsAsync(client, "missing_tool_task", prompt: "Say hello", timeoutMs: 12000, runCount, CancellationToken.None);
        await SeedScenarioRunsAsync(client, "multi_step_build", prompt: "Say hello", timeoutMs: 15000, runCount, CancellationToken.None);
        await SeedScenarioRunsAsync(client, "partial_failure_recovery", prompt: "Say hello", timeoutMs: 12000, runCount, CancellationToken.None);

        var sut = factory.Services.GetRequiredService<AutonomyScorecardService>();
        var report = sut.GenerateReport(runsPerScenario: runCount);

        var passed = MeetsThresholds(
            report,
            minCoverage: 1.0,
            minGrade: "B",
            minSuccessRatePerScenario: 0.80);

        passed.Should().BeTrue(FormatReport(report));
    }

    [Fact]
    public async Task EvaluateCertificationMode_ShouldPass_WhenRealAgentRunsMeetStrictContractAndCoverage()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();
        ConfigureOperatorHeader(factory, client);

        var runCount = 3;
        await SeedScenarioRunsAsync(client, "simple_search", prompt: "Say hello", timeoutMs: 12000, runCount, CancellationToken.None);
        await SeedScenarioRunsAsync(client, "missing_tool_task", prompt: "Say hello", timeoutMs: 12000, runCount, CancellationToken.None);
        await SeedScenarioRunsAsync(client, "multi_step_build", prompt: "Say hello", timeoutMs: 15000, runCount, CancellationToken.None);
        await SeedScenarioRunsAsync(client, "partial_failure_recovery", prompt: "Say hello", timeoutMs: 12000, runCount, CancellationToken.None);

        var sut = factory.Services.GetRequiredService<AutonomyScorecardService>();
        var report = sut.GenerateReport(new AutonomyScorecardOptions
        {
            RunsPerScenario = runCount,
            CertificationMode = true,
            FailOnInsufficientData = true,
            RequireStructuredOutcomeContract = true,
            MinCoverage = 1.0,
            MinGrade = "B",
            MinSuccessRatePerScenario = 0.80,
        });

        report.CertificationPassed.Should().BeTrue(FormatReport(report));
    }

    private static void ConfigureOperatorHeader(InfernalHierarchyTestWebAppFactory factory, HttpClient client)
    {
        var options = factory.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Infernal-Operator-Key");
            client.DefaultRequestHeaders.Add("X-Infernal-Operator-Key", options.ApiKey);
        }
    }

    private static async Task SeedScenarioRunsAsync(
        HttpClient client,
        string benchmarkId,
        string prompt,
        int timeoutMs,
        int runCount,
        CancellationToken ct)
    {
        var create = new PlaygroundScenarioCreateRequest(
            Name: $"bench-{benchmarkId}-{Guid.NewGuid():N}",
            Prompt: prompt,
            ToAgentId: "lucifer",
            TimeoutMs: timeoutMs,
            ExecutionProfile: "Research",
            Tags: new Dictionary<string, object>
            {
                ["benchmark_id"] = benchmarkId,
            });

        var createResponse = await client.PostAsJsonAsync("/api/playground/scenarios", create, cancellationToken: ct).ConfigureAwait(false);
        createResponse.EnsureSuccessStatusCode();

        var createPayload = await createResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var createDoc = JsonDocument.Parse(createPayload);
        var scenarioId = createDoc.RootElement
            .GetProperty("scenario")
            .GetProperty("scenarioId")
            .GetString();

        scenarioId.Should().NotBeNullOrWhiteSpace();

        for (var i = 0; i < runCount; i++)
        {
            var runResponse = await client.PostAsJsonAsync(
                $"/api/playground/scenarios/{scenarioId}/run",
                new PlaygroundScenarioRunRequest(Prompt: prompt, TimeoutMs: timeoutMs),
                ct).ConfigureAwait(false);
            runResponse.EnsureSuccessStatusCode();
        }
    }

    private static string FormatReport(AutonomyScorecardReport report)
    {
        var scenarios = string.Join(", ", report.Scenarios.Select(s =>
            $"{s.ScenarioId}:status={s.Status},success={s.SuccessRate:P0},p95={s.P95DurationMs:F0},score={s.Score:F1}"));

        return $"grade={report.Grade}, coverage={report.Coverage:P0}, overall={report.OverallScore:F1}, scenarios=[{scenarios}]";
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
