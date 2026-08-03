namespace InfernalHierarchy.Host.Observability;

internal sealed class AutonomyScorecardService
{
    private static readonly IReadOnlyList<AutonomyBenchmarkScenario> Benchmarks =
    [
        new("simple_search", "Simple Search", "Quick retrieval and concise synthesis.", "Research", 12000),
        new("missing_tool_task", "Missing Tool Closure", "Task starts with a capability gap and requires remediation.", "Research", 25000),
        new("multi_step_build", "Multi-step Build", "Plan, build, and verify a small delivery workflow.", "Build", 45000),
        new("partial_failure_recovery", "Partial Failure Recovery", "Continue execution after an injected transient failure.", "Build", 30000)
    ];

    private readonly IAgentPlaygroundService _playground;

    public AutonomyScorecardService(IAgentPlaygroundService playground)
    {
        _playground = playground;
    }

    public AutonomyScorecardReport GenerateReport(int runsPerScenario = 10)
    {
        var cappedRunsPerScenario = Math.Clamp(runsPerScenario, 1, 50);
        var knownScenarios = _playground.ListScenarios(1000);

        var scenarioScores = new List<AutonomyScenarioScore>(Benchmarks.Count);
        foreach (var benchmark in Benchmarks)
        {
            var matchingScenarioIds = knownScenarios
                .Where(s => IsBenchmarkScenario(s, benchmark.Id))
                .Select(s => s.ScenarioId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var runs = matchingScenarioIds
                .SelectMany(id => _playground.GetRuns(id, cappedRunsPerScenario))
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(cappedRunsPerScenario)
                .ToArray();

            if (runs.Length == 0)
            {
                scenarioScores.Add(new AutonomyScenarioScore(
                    benchmark.Id,
                    benchmark.Name,
                    benchmark.TargetExecutionProfile,
                    benchmark.TargetP95Ms,
                    Runs: 0,
                    SuccessRate: 0,
                    P95DurationMs: 0,
                    Score: null,
                    Status: "insufficient_data"));
                continue;
            }

            var successCount = runs.Count(IsSuccessfulRun);
            var successRate = successCount / (double)runs.Length;
            var p95 = CalculatePercentile(runs.Select(r => r.Response.durationMs).ToArray(), 95);
            var latencyScore = Math.Clamp(1d - (p95 / benchmark.TargetP95Ms), 0d, 1d) * 100d;
            var score = (successRate * 70d) + (latencyScore * 30d);

            scenarioScores.Add(new AutonomyScenarioScore(
                benchmark.Id,
                benchmark.Name,
                benchmark.TargetExecutionProfile,
                benchmark.TargetP95Ms,
                Runs: runs.Length,
                SuccessRate: successRate,
                P95DurationMs: p95,
                Score: score,
                Status: "evaluated"));
        }

        var scored = scenarioScores.Where(s => s.Score.HasValue).ToArray();
        var coverage = scenarioScores.Count == 0
            ? 0d
            : scored.Length / (double)scenarioScores.Count;
        var overall = scored.Length == 0 ? 0d : scored.Average(s => s.Score!.Value);

        var recommendations = BuildRecommendations(scenarioScores, coverage, overall);

        return new AutonomyScorecardReport(
            GeneratedAtUtc: DateTime.UtcNow,
            OverallScore: overall,
            Coverage: coverage,
            Grade: ToGrade(overall, coverage),
            Scenarios: scenarioScores,
            Recommendations: recommendations);
    }

    public IReadOnlyList<AutonomyBenchmarkScenario> GetBenchmarks() => Benchmarks;

    private static bool IsBenchmarkScenario(PlaygroundScenario scenario, string benchmarkId)
    {
        if (scenario.Tags.TryGetValue("benchmark_id", out var tagValue)
            && string.Equals(tagValue?.ToString(), benchmarkId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return scenario.Name.Contains(benchmarkId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessfulRun(PlaygroundRunRecord run)
    {
        var content = run.Response.content ?? string.Empty;
        if (content.StartsWith("Timeout:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (content.StartsWith('❌'))
        {
            return false;
        }

        return true;
    }

    private static double CalculatePercentile(IReadOnlyList<double> values, int percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.OrderBy(x => x).ToArray();
        var rank = Math.Clamp(percentile, 0, 100) / 100d * (ordered.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return ordered[lower];
        }

        var weight = rank - lower;
        return ordered[lower] + ((ordered[upper] - ordered[lower]) * weight);
    }

    private static IReadOnlyList<string> BuildRecommendations(
        IReadOnlyList<AutonomyScenarioScore> scenarios,
        double coverage,
        double overall)
    {
        var recommendations = new List<string>();

        if (coverage < 1d)
        {
            recommendations.Add("Run all benchmark scenarios at least once to reach full scorecard coverage.");
        }

        foreach (var scenario in scenarios.Where(s => s.Score.HasValue && s.SuccessRate < 0.80d))
        {
            recommendations.Add($"Improve reliability for '{scenario.ScenarioName}' (success rate {scenario.SuccessRate:P0}).");
        }

        foreach (var scenario in scenarios.Where(s => s.Score.HasValue && s.P95DurationMs > s.TargetP95Ms))
        {
            recommendations.Add($"Reduce p95 latency for '{scenario.ScenarioName}' ({scenario.P95DurationMs:F0}ms > target {scenario.TargetP95Ms:F0}ms).");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(overall >= 85
                ? "Autonomy scorecard is healthy; continue periodic regression runs."
                : "Scorecard is stable; prioritize low-latency and higher success consistency improvements.");
        }

        return recommendations;
    }

    private static string ToGrade(double overall, double coverage)
    {
        if (coverage < 0.5d)
        {
            return "incomplete";
        }

        return overall switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            _ => "E"
        };
    }
}

internal sealed record AutonomyBenchmarkScenario(
    string Id,
    string Name,
    string Description,
    string TargetExecutionProfile,
    double TargetP95Ms);

internal sealed record AutonomyScenarioScore(
    string ScenarioId,
    string ScenarioName,
    string TargetExecutionProfile,
    double TargetP95Ms,
    int Runs,
    double SuccessRate,
    double P95DurationMs,
    double? Score,
    string Status);

internal sealed record AutonomyScorecardReport(
    DateTime GeneratedAtUtc,
    double OverallScore,
    double Coverage,
    string Grade,
    IReadOnlyList<AutonomyScenarioScore> Scenarios,
    IReadOnlyList<string> Recommendations);