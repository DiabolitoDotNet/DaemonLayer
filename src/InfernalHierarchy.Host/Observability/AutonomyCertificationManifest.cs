namespace InfernalHierarchy.Host.Observability;

internal static class AutonomyCertificationManifest
{
    public const string Version = "2026.08";

    public static IReadOnlyList<AutonomyCertificationScenarioRequirement> Requirements { get; } =
    [
        new(
            BenchmarkId: "simple_search",
            RequiredCapabilities:
            [
                "workflow_step"
            ]),
        new(
            BenchmarkId: "missing_tool_task",
            RequiredCapabilities:
            [
                "request_collaboration",
                "workflow_step"
            ]),
        new(
            BenchmarkId: "multi_step_build",
            RequiredCapabilities:
            [
                "workflow_step",
                "request_collaboration"
            ]),
        new(
            BenchmarkId: "partial_failure_recovery",
            RequiredCapabilities:
            [
                "workflow_step",
                "request_collaboration"
            ])
    ];
}

internal sealed record AutonomyCertificationScenarioRequirement(
    string BenchmarkId,
    IReadOnlyList<string> RequiredCapabilities);
