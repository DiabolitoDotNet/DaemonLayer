using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Tools.Clients;
using InfernalHierarchy.Tools.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class DefaultReportGeneratorTests
{
    [Fact]
    public async Task GenerateUsageReportAsync_WhenTokenTrackerMissing_ReturnsWarning()
    {
        var generator = new DefaultReportGenerator(tokenUsageTracker: null, multiModelLlmClient: null);

        var report = await generator.GenerateUsageReportAsync(CancellationToken.None);

        Assert.Contains("Token usage tracking not available", report);
    }

    [Fact]
    public async Task GenerateUsageReportAsync_WhenStatsPresent_IncludesTotalsAndModelBreakdown()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);

        tracker.RecordUsage(new TokenUsageRecord
        {
            AgentId = "a1",
            ModelName = "mA",
            InputTokens = 10,
            OutputTokens = 5,
            Duration = TimeSpan.FromMilliseconds(100)
        });

        tracker.RecordUsage(new TokenUsageRecord
        {
            AgentId = "a1",
            ModelName = "mA",
            InputTokens = 4,
            OutputTokens = 1,
            Duration = TimeSpan.FromMilliseconds(50)
        });

        tracker.RecordUsage(new TokenUsageRecord
        {
            AgentId = "a2",
            ModelName = "mB",
            InputTokens = 3,
            OutputTokens = 2,
            Duration = TimeSpan.FromMilliseconds(25)
        });

        var generator = new DefaultReportGenerator(tracker, multiModelLlmClient: null);

        var report = await generator.GenerateUsageReportAsync(CancellationToken.None);

        Assert.Contains("Token Usage Statistics", report);
        Assert.Contains("Total Calls:", report);
        Assert.Contains("Per-Model Breakdown", report);

        // Ordering: mA has 2 calls, should appear before mB (1 call)
        var idxA = report.IndexOf("mA:", StringComparison.Ordinal);
        var idxB = report.IndexOf("mB:", StringComparison.Ordinal);
        Assert.True(idxA >= 0);
        Assert.True(idxB >= 0);
        Assert.True(idxA < idxB);
    }

    [Fact]
    public async Task GenerateModelsReportAsync_WhenClientMissing_ReturnsWarning()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);
        var generator = new DefaultReportGenerator(tracker, multiModelLlmClient: null);

        var report = await generator.GenerateModelsReportAsync(CancellationToken.None);

        Assert.Contains("LLM model information not available", report);
    }

    [Fact]
    public async Task GenerateModelsReportAsync_WhenModelsConfigured_ListsModelsInPriorityOrder()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);

        var options = Options.Create(new LlmOptions
        {
            Models = new()
            {
                new ModelConfig
                {
                    Name = "model-b",
                    Priority = 2,
                    Complexity = TaskComplexity.Medium,
                    MaxTokens = 2000,
                    Temperature = 0.2,
                    BaseUrl = new Uri("http://localhost:11434/v1")
                },
                new ModelConfig
                {
                    Name = "model-a",
                    Priority = 1,
                    Complexity = TaskComplexity.Simple,
                    MaxTokens = 1000,
                    Temperature = 0.7,
                    BaseUrl = new Uri("http://localhost:11434/v1")
                }
            }
        });

        using var client = new MultiModelLlmClient(options, tracker, NullLogger<MultiModelLlmClient>.Instance);

        var generator = new DefaultReportGenerator(tracker, client);

        var report = await generator.GenerateModelsReportAsync(CancellationToken.None);

        Assert.Contains("Available LLM Models", report);

        // Priority order: model-a (1) should appear before model-b (2)
        var idxA = report.IndexOf("**model-a**", StringComparison.Ordinal);
        var idxB = report.IndexOf("**model-b**", StringComparison.Ordinal);
        Assert.True(idxA >= 0);
        Assert.True(idxB >= 0);
        Assert.True(idxA < idxB);

        Assert.Contains("Max Tokens:", report);
        Assert.Contains("Temperature:", report);
    }
}
