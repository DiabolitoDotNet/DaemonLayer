using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class TokenUsageTrackerTests
{
    [Fact]
    public void GetOverallStats_WhenEmpty_ReturnsZeros()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);

        var stats = tracker.GetOverallStats();

        stats.TotalCalls.Should().Be(0);
        stats.TotalInputTokens.Should().Be(0);
        stats.TotalOutputTokens.Should().Be(0);
        stats.TotalTokens.Should().Be(0);
        stats.AverageDuration.Should().Be(TimeSpan.Zero);
        stats.ModelBreakdown.Should().NotBeNull();
        stats.ModelBreakdown!.Should().BeEmpty();
    }

    [Fact]
    public void RecordUsage_ShouldUpdateOverallAndModelStats()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);

        tracker.RecordUsage(new TokenUsageRecord
        {
            Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModelName = "m1",
            AgentId = "a1",
            InputTokens = 100,
            OutputTokens = 50,
            Duration = TimeSpan.FromMilliseconds(500)
        });

        tracker.RecordUsage(new TokenUsageRecord
        {
            Timestamp = new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc),
            ModelName = "m1",
            AgentId = "a2",
            InputTokens = 200,
            OutputTokens = 100,
            Duration = TimeSpan.FromMilliseconds(1500)
        });

        var overall = tracker.GetOverallStats();
        overall.TotalCalls.Should().Be(2);
        overall.TotalInputTokens.Should().Be(300);
        overall.TotalOutputTokens.Should().Be(150);
        overall.TotalTokens.Should().Be(450);
        overall.AverageDuration.Should().Be(TimeSpan.FromMilliseconds(1000));

        overall.ModelBreakdown.Should().ContainKey("m1");
        var m1 = overall.ModelBreakdown!["m1"];
        m1.CallCount.Should().Be(2);
        m1.TotalInputTokens.Should().Be(300);
        m1.TotalOutputTokens.Should().Be(150);
        m1.TotalDuration.Should().Be(TimeSpan.FromMilliseconds(2000));
        m1.AverageDuration.Should().Be(TimeSpan.FromMilliseconds(1000));
        m1.TokensPerSecond.Should().BeGreaterThan(0);

        tracker.GetModelStats("m1").Should().NotBeNull();
        tracker.GetModelStats("missing").Should().BeNull();
    }

    [Fact]
    public void GetAgentStats_ShouldFilterByAgentId()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);

        tracker.RecordUsage(new TokenUsageRecord { ModelName = "m1", AgentId = "a1", InputTokens = 10, OutputTokens = 5, Duration = TimeSpan.FromMilliseconds(100) });
        tracker.RecordUsage(new TokenUsageRecord { ModelName = "m2", AgentId = "a1", InputTokens = 20, OutputTokens = 10, Duration = TimeSpan.FromMilliseconds(300) });
        tracker.RecordUsage(new TokenUsageRecord { ModelName = "m1", AgentId = "a2", InputTokens = 999, OutputTokens = 999, Duration = TimeSpan.FromMilliseconds(999) });

        var a1 = tracker.GetAgentStats("a1");
        a1.TotalCalls.Should().Be(2);
        a1.TotalInputTokens.Should().Be(30);
        a1.TotalOutputTokens.Should().Be(15);
        a1.TotalTokens.Should().Be(45);
        a1.AverageDuration.Should().Be(TimeSpan.FromMilliseconds(200));

        tracker.GetAgentStats("missing").TotalCalls.Should().Be(0);
    }

    [Fact]
    public void CalculateEstimatedCost_ShouldUsePricingPerModel()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);

        tracker.RecordUsage(new TokenUsageRecord { ModelName = "m1", AgentId = "a", InputTokens = 1_000_000, OutputTokens = 2_000_000, Duration = TimeSpan.FromSeconds(1) });
        tracker.RecordUsage(new TokenUsageRecord { ModelName = "m2", AgentId = "a", InputTokens = 500_000, OutputTokens = 500_000, Duration = TimeSpan.FromSeconds(1) });

        var pricing = new Dictionary<string, ModelPricing>
        {
            ["m1"] = new ModelPricing { ModelName = "m1", InputPricePerMillion = 1.5m, OutputPricePerMillion = 3.0m },
            ["m2"] = new ModelPricing { ModelName = "m2", InputPricePerMillion = 2.0m, OutputPricePerMillion = 4.0m }
        };

        var cost = tracker.CalculateEstimatedCost(pricing);

        // m1: 1M in * 1.5 + 2M out * 3.0 = 7.5
        // m2: 0.5M in * 2.0 + 0.5M out * 4.0 = 3.0
        cost.Should().Be(10.5m);
    }

    [Fact]
    public void Reset_ShouldClearAllStats()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);
        tracker.RecordUsage(new TokenUsageRecord { ModelName = "m", AgentId = "a", InputTokens = 1, OutputTokens = 1, Duration = TimeSpan.FromMilliseconds(1) });

        tracker.Reset();

        tracker.GetOverallStats().TotalCalls.Should().Be(0);
        tracker.GetOverallStats().ModelBreakdown.Should().NotBeNull();
        tracker.GetOverallStats().ModelBreakdown!.Should().BeEmpty();
        tracker.GetRecentRecords(10).Should().BeEmpty();
    }

    [Fact]
    public void GetRecentRecords_ShouldReturnNewestFirst()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);

        tracker.RecordUsage(new TokenUsageRecord { Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ModelName = "m", AgentId = "a", InputTokens = 1, OutputTokens = 1, Duration = TimeSpan.FromMilliseconds(1) });
        tracker.RecordUsage(new TokenUsageRecord { Timestamp = new DateTime(2025, 1, 1, 0, 0, 2, DateTimeKind.Utc), ModelName = "m", AgentId = "a", InputTokens = 1, OutputTokens = 1, Duration = TimeSpan.FromMilliseconds(1) });
        tracker.RecordUsage(new TokenUsageRecord { Timestamp = new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc), ModelName = "m", AgentId = "a", InputTokens = 1, OutputTokens = 1, Duration = TimeSpan.FromMilliseconds(1) });

        var recent = tracker.GetRecentRecords(2).ToList();

        recent.Should().HaveCount(2);
        recent[0].Timestamp.Should().Be(new DateTime(2025, 1, 1, 0, 0, 2, DateTimeKind.Utc));
        recent[1].Timestamp.Should().Be(new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc));
    }

    [Fact]
    public void GetOptimizationReport_ShouldFlagHighLatencyModels()
    {
        var tracker = new TokenUsageTracker(NullLogger<TokenUsageTracker>.Instance);

        for (var i = 0; i < 4; i++)
        {
            tracker.RecordUsage(new TokenUsageRecord
            {
                ModelName = "slow-model",
                AgentId = "a",
                InputTokens = 100,
                OutputTokens = 100,
                Duration = TimeSpan.FromMilliseconds(7000)
            });

            tracker.RecordUsage(new TokenUsageRecord
            {
                ModelName = "fast-model",
                AgentId = "a",
                InputTokens = 100,
                OutputTokens = 100,
                Duration = TimeSpan.FromMilliseconds(800)
            });
        }

        var report = tracker.GetOptimizationReport(highLatencyThresholdMs: 5000, minCalls: 3, topN: 10);

        report.Items.Should().NotBeEmpty();
        report.Items.Should().Contain(x => x.ModelName == "slow-model" && x.IsHighLatencyOrCost);
        report.Items.Should().Contain(x => x.ModelName == "fast-model" && !x.IsHighLatencyOrCost);
    }
}
