using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class PerfGateRealPathScenarioGuardTests
{
    [Fact]
    public void PerfBaseline_ShouldKeepAutonomyRealPathScenarios()
    {
        var repoRoot = ResolveRepositoryRoot();
        var baselinePath = Path.Combine(repoRoot, "tools", "InfernalHierarchy.PerfGate", "perf-baseline.json");

        File.Exists(baselinePath).Should().BeTrue($"perf baseline file should exist at {baselinePath}");

        using var json = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var root = json.RootElement;

        root.TryGetProperty("autonomyChatRoundTripPath", out _).Should().BeTrue();
        root.TryGetProperty("autonomyChatRoundTripDegradedPath", out _).Should().BeTrue();

        var scenarios = root.GetProperty("trendComparison").GetProperty("scenarios")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        scenarios.Should().Contain("autonomyChatRoundTripPath");
        scenarios.Should().Contain("autonomyChatRoundTripDegradedPath");
    }

    [Fact]
    public void PerfBaseline_ShouldKeepHotPathEvidenceScenariosAndBudgets()
    {
        var repoRoot = ResolveRepositoryRoot();
        var baselinePath = Path.Combine(repoRoot, "tools", "InfernalHierarchy.PerfGate", "perf-baseline.json");

        File.Exists(baselinePath).Should().BeTrue($"perf baseline file should exist at {baselinePath}");

        using var json = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var root = json.RootElement;

        // Hot paths that must always remain perf-evidence-backed.
        root.TryGetProperty("autonomyScorecardReport", out var scorecardBudget).Should().BeTrue();
        root.TryGetProperty("autonomyCertificationTailLatency", out var tailLatencyBudget).Should().BeTrue();

        scorecardBudget.TryGetProperty("maxLatencyPerOpMs", out var scorecardLatency).Should().BeTrue();
        scorecardBudget.TryGetProperty("maxAllocatedBytesPerOp", out var scorecardAlloc).Should().BeTrue();
        tailLatencyBudget.TryGetProperty("maxLatencyPerOpMs", out var tailLatency).Should().BeTrue();
        tailLatencyBudget.TryGetProperty("maxAllocatedBytesPerOp", out var tailAlloc).Should().BeTrue();

        scorecardLatency.GetDouble().Should().BeGreaterThan(0);
        scorecardAlloc.GetDouble().Should().BeGreaterThan(0);
        tailLatency.GetDouble().Should().BeGreaterThan(0);
        tailAlloc.GetDouble().Should().BeGreaterThan(0);

        var scenarios = root.GetProperty("trendComparison").GetProperty("scenarios")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        scenarios.Should().Contain("autonomyChatRoundTripPath");
        scenarios.Should().Contain("autonomyChatRoundTripDegradedPath");
        scenarios.Should().Contain("autonomyCertificationTailLatency");
    }

    private static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var sln = Path.Combine(dir.FullName, "InfernalHierarchy.sln");
            if (File.Exists(sln))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root from test base directory.");
    }
}
