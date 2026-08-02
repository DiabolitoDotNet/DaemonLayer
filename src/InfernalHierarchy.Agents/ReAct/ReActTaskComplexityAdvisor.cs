using System.Text.RegularExpressions;

namespace InfernalHierarchy.Agents.ReAct;

public enum ReActTaskComplexity
{
    Simple,
    Medium,
    Complex
}

public sealed record ReActComplexityAssessment(
    ReActTaskComplexity Complexity,
    int IterationBudget,
    int RecommendedParallelBranches,
    string ReasonCode);

public static class ReActTaskComplexityAdvisor
{
    private static readonly Regex ComplexityKeywordRegex = new(
        @"\b(refactor|architecture|deploy|migration|rollback|orchestr|pipeline|integration|multi[- ]?step|parallel|sub-?agent|team|collaborat|production|performance|benchmark|incident|postmortem)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ReActComplexityAssessment Assess(
        string task,
        IReadOnlyCollection<string> availableTools,
        string? executionProfile,
        ReActOptions options)
    {
        var content = task ?? string.Empty;
        var length = content.Length;

        var profile = (executionProfile ?? string.Empty).Trim();
        var hasBuildOrDeployProfile = profile.Equals("Build", StringComparison.OrdinalIgnoreCase)
            || profile.Equals("Deploy", StringComparison.OrdinalIgnoreCase);

        var hasExecutionTools = availableTools.Contains("python_exec", StringComparer.OrdinalIgnoreCase)
            || availableTools.Contains("node_exec", StringComparer.OrdinalIgnoreCase)
            || availableTools.Contains("workflow_step", StringComparer.OrdinalIgnoreCase)
            || availableTools.Contains("deploy_adapter", StringComparer.OrdinalIgnoreCase);

        var keywordHits = ComplexityKeywordRegex.Matches(content).Count;

        var complexity = ReActTaskComplexity.Medium;
        var reasonCode = "default_medium";

        if (length < 120 && keywordHits == 0 && !hasBuildOrDeployProfile)
        {
            complexity = ReActTaskComplexity.Simple;
            reasonCode = "short_low_risk_prompt";
        }
        else if (length > 600 || keywordHits >= 2 || hasBuildOrDeployProfile || hasExecutionTools)
        {
            complexity = ReActTaskComplexity.Complex;
            reasonCode = hasBuildOrDeployProfile
                ? "profile_build_or_deploy"
                : (keywordHits >= 2 ? "complex_keyword_density" : "execution_tools_present");
        }

        var baseIterations = complexity switch
        {
            ReActTaskComplexity.Simple => options.SimpleTaskMaxIterations,
            ReActTaskComplexity.Complex => options.ComplexTaskMaxIterations,
            _ => options.MediumTaskMaxIterations
        };

        var boundedIterations = Math.Clamp(baseIterations, 1, Math.Max(1, options.HardMaxIterations));

        var recommendedParallelBranches = complexity switch
        {
            ReActTaskComplexity.Simple => 1,
            ReActTaskComplexity.Complex => Math.Max(2, options.MaxParallelBranches),
            _ => Math.Max(1, Math.Min(2, options.MaxParallelBranches))
        };

        return new ReActComplexityAssessment(
            Complexity: complexity,
            IterationBudget: boundedIterations,
            RecommendedParallelBranches: recommendedParallelBranches,
            ReasonCode: reasonCode);
    }
}