using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class ToolRateLimitingTests
{
    private sealed class FakeTool : ITool
    {
        public FakeTool(string name) => Name = name;

        public string Name { get; }

        public string Description => "fake";

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default) =>
            Task.FromResult(new ToolResult { Success = true, Output = "ok" });
    }

    [Fact]
    public async Task Pipeline_WhenRateLimited_ReturnsFailureWithRetryAfterMetadata()
    {
        var options = MsOptions.Create(new ToolRateLimitingOptions
        {
            Enabled = true,
            DefaultRule = new FixedWindowRateLimitRule { PermitLimit = 2, WindowSeconds = 60 },
            RankDefaults = new Dictionary<string, FixedWindowRateLimitRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["Worker"] = new FixedWindowRateLimitRule { PermitLimit = 2, WindowSeconds = 60 }
            },
            Tools = new Dictionary<string, ToolRateLimitOverride>(StringComparer.OrdinalIgnoreCase)
        });

        var limiter = new FixedWindowToolRateLimiter(options, NullLogger<FixedWindowToolRateLimiter>.Instance);

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: null,
            eventSink: null,
            rateLimiter: limiter);

        var tool = new FakeTool("web_search");
        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object>(),
            AgentId: "agent-1",
            AgentRank: "Worker",
            CancellationToken: CancellationToken.None);

        var r1 = await pipeline.ExecuteAsync(context);
        var r2 = await pipeline.ExecuteAsync(context);
        var r3 = await pipeline.ExecuteAsync(context);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.False(r3.Success);
        Assert.NotNull(r3.Error);
        Assert.True(r3.Metadata.TryGetValue("rate_limited", out var rateLimited) && rateLimited is bool b && b);
        Assert.True(r3.Metadata.TryGetValue("retry_after_ms", out var retryAfter) && retryAfter is long ms && ms > 0);
    }

    [Fact]
    public async Task Pipeline_RateLimitIsPerAgent()
    {
        var options = MsOptions.Create(new ToolRateLimitingOptions
        {
            Enabled = true,
            DefaultRule = new FixedWindowRateLimitRule { PermitLimit = 1, WindowSeconds = 60 },
            RankDefaults = new Dictionary<string, FixedWindowRateLimitRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["Worker"] = new FixedWindowRateLimitRule { PermitLimit = 1, WindowSeconds = 60 }
            }
        });

        var limiter = new FixedWindowToolRateLimiter(options, NullLogger<FixedWindowToolRateLimiter>.Instance);

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: null,
            eventSink: null,
            rateLimiter: limiter);

        var tool = new FakeTool("read_memory");

        var a1 = new ToolExecutionContext(tool.Name, tool, new(), "agent-1", "Worker", CancellationToken.None);
        var a2 = new ToolExecutionContext(tool.Name, tool, new(), "agent-2", "Worker", CancellationToken.None);

        var r1 = await pipeline.ExecuteAsync(a1);
        var r2 = await pipeline.ExecuteAsync(a2);
        var r3 = await pipeline.ExecuteAsync(a1);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.False(r3.Success);
    }

    [Fact]
    public async Task Pipeline_ToolOverride_CanDisableRateLimitingForSpecificTool()
    {
        var options = MsOptions.Create(new ToolRateLimitingOptions
        {
            Enabled = true,
            DefaultRule = new FixedWindowRateLimitRule { PermitLimit = 1, WindowSeconds = 60 },
            RankDefaults = new Dictionary<string, FixedWindowRateLimitRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["Worker"] = new FixedWindowRateLimitRule { PermitLimit = 1, WindowSeconds = 60 }
            },
            Tools = new Dictionary<string, ToolRateLimitOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["telegram_send"] = new ToolRateLimitOverride { Enabled = false }
            }
        });

        var limiter = new FixedWindowToolRateLimiter(options, NullLogger<FixedWindowToolRateLimiter>.Instance);

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: null,
            eventSink: null,
            rateLimiter: limiter);

        var tool = new FakeTool("telegram_send");
        var context = new ToolExecutionContext(tool.Name, tool, new(), "agent-1", "Worker", CancellationToken.None);

        var r1 = await pipeline.ExecuteAsync(context);
        var r2 = await pipeline.ExecuteAsync(context);
        var r3 = await pipeline.ExecuteAsync(context);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.True(r3.Success);
    }
}
