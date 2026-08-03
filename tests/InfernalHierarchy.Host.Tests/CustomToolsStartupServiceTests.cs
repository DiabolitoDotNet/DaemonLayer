using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Tools;
using InfernalHierarchy.Tools.Dynamic;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class CustomToolsStartupServiceTests
{
    [Fact]
    public async Task StartAsync_LoadsPolicyFlaggedToolWithoutManualApprovalGate()
    {
        var definition = new CustomToolDefinition
        {
            Id = "tool-1",
            ToolName = "custom_http",
            Description = "network tool",
            SourceCode = "using System.Net.Http;",
            CreatedByAgentId = "lucifer",
            CreatedByAgentName = "Lucifer",
            RequiresManualApproval = true
        };

        var store = new InMemoryCustomToolStore(definition);
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var compiler = new StubCompiler(new HttpTool());
        var policy = new StubPolicy(new CustomToolPolicyDecision(
            Allowed: true,
            RequiresManualApproval: true,
            Reason: "network",
            MatchedRules: new[] { "HttpClient/WebRequest" }));

        var sut = new CustomToolsStartupService(
            store,
            registry,
            new ServiceCollection().BuildServiceProvider(),
            compiler,
            policy,
            new TestOptionsMonitor<CustomToolsOptions>(new CustomToolsOptions
            {
                Enabled = true,
                AllowUnsafeWithoutManualApproval = false,
                AllowNetworkWithoutManualApproval = false
            }),
            NullLogger<CustomToolsStartupService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        registry.GetTool("custom_http").Should().NotBeNull();

        var persisted = await store.GetByNameAsync("custom_http");
        persisted.Should().NotBeNull();
        persisted!.LastCompiledAt.Should().NotBeNull();
        persisted.LastCompileError.Should().BeNull();
    }

    private sealed class HttpTool : ITool
    {
        public string Name => "custom_http";
        public string Description => "http";

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Success = true, Output = "ok" });
    }

    private sealed class StubCompiler : ICustomToolCompiler
    {
        private readonly ITool _tool;

        public StubCompiler(ITool tool) => _tool = tool;

        public Task<CustomToolCompileResult> CompileAndCreateAsync(
            string sourceCode,
            string? expectedToolName,
            IServiceProvider services,
            ILogger logger,
            CancellationToken ct = default)
            => Task.FromResult(new CustomToolCompileResult(true, _tool, null, Array.Empty<string>()));
    }

    private sealed class StubPolicy : ICustomToolSecurityPolicy
    {
        private readonly CustomToolPolicyDecision _decision;

        public StubPolicy(CustomToolPolicyDecision decision) => _decision = decision;

        public CustomToolPolicyDecision Evaluate(string sourceCode) => _decision;
    }

    private sealed class InMemoryCustomToolStore : ICustomToolStore
    {
        private readonly Dictionary<string, CustomToolDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _idByName = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryCustomToolStore(params CustomToolDefinition[] initial)
        {
            foreach (var tool in initial)
            {
                _byId[tool.Id] = tool;
                _idByName[tool.ToolName] = tool.Id;
            }
        }

        public Task UpsertAsync(CustomToolDefinition tool, CancellationToken ct = default)
        {
            _byId[tool.Id] = tool;
            _idByName[tool.ToolName] = tool.Id;
            return Task.CompletedTask;
        }

        public Task<CustomToolDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            _byId.TryGetValue(id, out var tool);
            return Task.FromResult<CustomToolDefinition?>(tool);
        }

        public Task<CustomToolDefinition?> GetByNameAsync(string toolName, CancellationToken ct = default)
        {
            if (_idByName.TryGetValue(toolName, out var id) && _byId.TryGetValue(id, out var tool))
            {
                return Task.FromResult<CustomToolDefinition?>(tool);
            }

            return Task.FromResult<CustomToolDefinition?>(null);
        }

        public Task<IReadOnlyList<CustomToolDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CustomToolDefinition>>(_byId.Values.ToList());

        public Task<bool> DeleteByIdAsync(string id, CancellationToken ct = default)
        {
            if (!_byId.Remove(id, out var removed))
            {
                return Task.FromResult(false);
            }

            _idByName.Remove(removed.ToolName);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteByNameAsync(string toolName, CancellationToken ct = default)
        {
            if (!_idByName.TryGetValue(toolName, out var id))
            {
                return Task.FromResult(false);
            }

            _idByName.Remove(toolName);
            return Task.FromResult(_byId.Remove(id));
        }
    }

    private sealed class TestOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        private readonly T _current;

        public TestOptionsMonitor(T current) => _current = current;

        public T CurrentValue => _current;

        public T Get(string? name) => _current;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
