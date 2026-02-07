using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace InfernalHierarchy.Tools.Tests;

public sealed class ToolResultCachingTests
{
    private sealed class CountingTool : ITool
    {
        private int _count;

        public CountingTool(string name) => Name = name;

        public string Name { get; }

        public string Description => "counting";

        public int CallCount => Volatile.Read(ref _count);

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new ToolResult
            {
                Success = true,
                Output = $"ok-{CallCount}"
            });
        }
    }

    private sealed class InMemoryToolResultCacheStore : IToolResultCacheStore
    {
        private readonly ConcurrentDictionary<string, CachedToolResult> _cache = new(StringComparer.Ordinal);

        public Task<CachedToolResult?> GetAsync(string inputKey, CancellationToken ct = default)
        {
            if (!_cache.TryGetValue(inputKey, out var entry))
            {
                return Task.FromResult<CachedToolResult?>(null);
            }

            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _cache.TryRemove(inputKey, out _);
                return Task.FromResult<CachedToolResult?>(null);
            }

            return Task.FromResult<CachedToolResult?>(entry);
        }

        public Task UpsertAsync(CachedToolResult entry, CancellationToken ct = default)
        {
            _cache[entry.InputKey] = entry;
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string inputKey, CancellationToken ct = default)
        {
            return Task.FromResult(_cache.TryRemove(inputKey, out _));
        }

        public Task<int> PruneExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
        {
            var removed = 0;
            foreach (var (k, v) in _cache)
            {
                if (v.ExpiresAt <= now && _cache.TryRemove(k, out _))
                {
                    removed++;
                }
            }

            return Task.FromResult(removed);
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            _cache.Clear();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Pipeline_WhenCacheEnabled_ReturnsCachedResult_OnSecondCall()
    {
        var tool = new CountingTool("web_search");
        var cacheStore = new InMemoryToolResultCacheStore();
        var cacheOptions = MsOptions.Create(new ToolResultCacheOptions
        {
            Enabled = true,
            DefaultTtl = TimeSpan.FromMinutes(5),
            CacheableTools = new List<string> { "web_search" }
        });

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: null,
            eventSink: null,
            rateLimiter: null,
            authorizationService: null,
            cacheStore: cacheStore,
            cacheOptions: cacheOptions);

        var context = new ToolExecutionContext(
            ToolName: tool.Name,
            Tool: tool,
            Parameters: new Dictionary<string, object> { ["q"] = "test" },
            AgentId: "agent-1",
            AgentRank: "Worker",
            CancellationToken: CancellationToken.None);

        var r1 = await pipeline.ExecuteAsync(context);
        var r2 = await pipeline.ExecuteAsync(context);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.Equal("ok-1", r1.Output);
        Assert.Equal("ok-1", r2.Output);
        Assert.Equal(1, tool.CallCount);
        Assert.True(r2.Metadata.TryGetValue("cache_hit", out var hit) && hit is bool b && b);
    }

    [Fact]
    public async Task Pipeline_WhenCacheBypassRequested_DoesNotUseCache()
    {
        var tool = new CountingTool("web_search");
        var cacheStore = new InMemoryToolResultCacheStore();
        var cacheOptions = MsOptions.Create(new ToolResultCacheOptions
        {
            Enabled = true,
            DefaultTtl = TimeSpan.FromMinutes(5),
            CacheableTools = new List<string> { "web_search" }
        });

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: null,
            eventSink: null,
            rateLimiter: null,
            authorizationService: null,
            cacheStore: cacheStore,
            cacheOptions: cacheOptions);

        var context1 = new ToolExecutionContext(tool.Name, tool, new Dictionary<string, object> { ["q"] = "x" }, "agent-1", "Worker", CancellationToken.None);
        var context2 = new ToolExecutionContext(tool.Name, tool, new Dictionary<string, object> { ["q"] = "x", ["cache_bust"] = true }, "agent-1", "Worker", CancellationToken.None);

        var r1 = await pipeline.ExecuteAsync(context1);
        var r2 = await pipeline.ExecuteAsync(context2);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.Equal("ok-1", r1.Output);
        Assert.Equal("ok-2", r2.Output);
        Assert.Equal(2, tool.CallCount);
    }

    [Fact]
    public async Task Pipeline_WhenCachedEntryExpired_TreatsAsMiss()
    {
        var tool = new CountingTool("web_search");
        var cacheStore = new InMemoryToolResultCacheStore();
        var cacheOptions = MsOptions.Create(new ToolResultCacheOptions
        {
            Enabled = true,
            DefaultTtl = TimeSpan.FromMinutes(5),
            CacheableTools = new List<string> { "web_search" }
        });

        var pipeline = new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService: null,
            exceptionHandler: null,
            eventSink: null,
            rateLimiter: null,
            authorizationService: null,
            cacheStore: cacheStore,
            cacheOptions: cacheOptions);

        var context = new ToolExecutionContext(tool.Name, tool, new Dictionary<string, object> { ["q"] = "y" }, "agent-1", "Worker", CancellationToken.None);

        // Seed an expired entry under the same computed key.
        var r1 = await pipeline.ExecuteAsync(context);
        Assert.Equal(1, tool.CallCount);

        Assert.True(r1.Metadata.TryGetValue("cache_key", out var keyObj));
        var key = Assert.IsType<string>(keyObj);
        Assert.False(string.IsNullOrWhiteSpace(key));
        await cacheStore.UpsertAsync(new CachedToolResult
        {
            ToolName = tool.Name,
            InputKey = key,
            ResultJson = "{\"success\":true,\"output\":\"expired\",\"metadata\":{}}",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        });

        var r2 = await pipeline.ExecuteAsync(context);
        Assert.Equal("ok-2", r2.Output);
        Assert.Equal(2, tool.CallCount);
    }
}
