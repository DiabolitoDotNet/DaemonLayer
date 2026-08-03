using System.Text;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultReportGenerator : IReportGenerator
{
    private readonly TokenUsageTracker? _tokenUsageTracker;
    private readonly MultiModelLlmClient? _multiModelLlmClient;

    public DefaultReportGenerator(TokenUsageTracker? tokenUsageTracker, MultiModelLlmClient? multiModelLlmClient)
    {
        _tokenUsageTracker = tokenUsageTracker;
        _multiModelLlmClient = multiModelLlmClient;
    }

    public Task<string> GenerateUsageReportAsync(CancellationToken ct)
    {
        if (_tokenUsageTracker == null)
        {
            return Task.FromResult("⚠️ Token usage tracking not available");
        }

        var stats = _tokenUsageTracker.GetOverallStats();

        var report = new StringBuilder();
        report.AppendLine("📊 **Token Usage Statistics**\n");
        report.AppendLine($"**Total Calls:** {stats.TotalCalls:N0}");
        report.AppendLine($"**Input Tokens:** {stats.TotalInputTokens:N0}");
        report.AppendLine($"**Output Tokens:** {stats.TotalOutputTokens:N0}");
        report.AppendLine($"**Total Tokens:** {stats.TotalTokens:N0}");
        report.AppendLine($"**Avg Duration:** {stats.AverageDuration.TotalMilliseconds:F0}ms\n");

        if (stats.ModelBreakdown is { Count: > 0 })
        {
            report.AppendLine("**Per-Model Breakdown:**");
            foreach (var kvp in stats.ModelBreakdown.OrderByDescending(x => x.Value.CallCount))
            {
                var totalTokens = kvp.Value.TotalInputTokens + kvp.Value.TotalOutputTokens;
                report.AppendLine($"  • {kvp.Key}: {kvp.Value.CallCount:N0} calls, {totalTokens:N0} tokens");
            }
        }

        return Task.FromResult(report.ToString());
    }

    public Task<string> GenerateModelsReportAsync(CancellationToken ct)
    {
        if (_multiModelLlmClient == null)
        {
            return Task.FromResult("⚠️ LLM model information not available");
        }

        var models = _multiModelLlmClient.GetAvailableModels();

        var report = new StringBuilder();
        report.AppendLine("🧠 **Available LLM Models**\n");

        foreach (var model in models)
        {
            report.AppendLine($"**{model.Name}**");
            report.AppendLine($"  Complexity: {model.Complexity}");
            report.AppendLine($"  Max Tokens: {model.MaxTokens:N0}");
            report.AppendLine($"  Temperature: {model.Temperature}");
            report.AppendLine();
        }

        return Task.FromResult(report.ToString());
    }
}
