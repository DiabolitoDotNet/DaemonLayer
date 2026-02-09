using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Dynamic;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Meta;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class CreateCustomToolToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithSafeCode_ShouldPersistCompileAndRegister()
    {
        var store = new InMemoryCustomToolStore();
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var services = new ServiceCollection().BuildServiceProvider();

        var llm = new StubLlmClient(SafeToolSource("custom_hello", "CustomHelloTool"));
        var compiler = new AssertingCompiler((source, expectedName) =>
        {
            source.Should().Contain("custom_hello");
            expectedName.Should().Be("custom_hello");
            return new HelloTool();
        });

        var tool = new CreateCustomToolTool(
            llm,
            registry,
            services,
            compiler,
            new DefaultCustomToolSecurityPolicy(),
            store,
            new TestOptionsMonitor<CustomToolsOptions>(new CustomToolsOptions { Enabled = true }),
            NullLogger<CreateCustomToolTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["requirement"] = "Say hello",
            ["tool_name"] = "custom_hello",
            ["agent_id"] = "lucifer",
            ["agent_name"] = "Lucifer"
        });

        result.Success.Should().BeTrue();
        registry.GetTool("custom_hello").Should().NotBeNull();

        var persisted = await store.GetByNameAsync("custom_hello");
        persisted.Should().NotBeNull();
        persisted!.RequiresManualApproval.Should().BeFalse();
        persisted.LastCompiledAt.Should().NotBeNull();
        persisted.LastCompileError.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithRiskyCode_ShouldPersistButNotRegister_WithoutApproval()
    {
        var store = new InMemoryCustomToolStore();
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var services = new ServiceCollection().BuildServiceProvider();

        var llm = new StubLlmClient(RiskyToolSource("custom_http", "CustomHttpTool"));
        var compiler = new AssertingCompiler((_, _) => throw new InvalidOperationException("Compiler should not be called"));

        var tool = new CreateCustomToolTool(
            llm,
            registry,
            services,
            compiler,
            new DefaultCustomToolSecurityPolicy(),
            store,
            new TestOptionsMonitor<CustomToolsOptions>(new CustomToolsOptions
            {
                Enabled = true,
                AllowUnsafeWithoutManualApproval = false
            }),
            NullLogger<CreateCustomToolTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["requirement"] = "Make an HTTP request",
            ["tool_name"] = "custom_http",
            ["agent_id"] = "lucifer",
            ["agent_name"] = "Lucifer"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("manual approval");
        registry.GetTool("custom_http").Should().BeNull();

        var persisted = await store.GetByNameAsync("custom_http");
        persisted.Should().NotBeNull();
        persisted!.RequiresManualApproval.Should().BeTrue();
    }

    private sealed class HelloTool : ITool
    {
        public string Name => "custom_hello";
        public string Description => "hello";
        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Success = true, Output = "hello" });
    }

    private sealed class StubLlmClient : ILlmClient
    {
        private readonly string _response;

        public StubLlmClient(string response) => _response = response;

        public Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
            => Task.FromResult(_response);

        public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult(_response);
    }

    private sealed class AssertingCompiler : ICustomToolCompiler
    {
        private readonly Func<string, string?, ITool> _factory;

        public AssertingCompiler(Func<string, string?, ITool> factory) => _factory = factory;

        public Task<CustomToolCompileResult> CompileAndCreateAsync(
            string sourceCode,
            string? expectedToolName,
            IServiceProvider services,
            ILogger logger,
            CancellationToken ct = default)
        {
            var tool = _factory(sourceCode, expectedToolName);
            return Task.FromResult(new CustomToolCompileResult(true, tool, null, Array.Empty<string>()));
        }
    }

    private sealed class InMemoryCustomToolStore : ICustomToolStore
    {
        private readonly Dictionary<string, CustomToolDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _idByName = new(StringComparer.OrdinalIgnoreCase);

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
    }

    private sealed class TestOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        private readonly T _current;

        public TestOptionsMonitor(T current) => _current = current;

        public T CurrentValue => _current;

        public T Get(string? name) => _current;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static string SafeToolSource(string toolName, string className) =>
        $"```csharp\n" +
        $"using InfernalHierarchy.Core.Interfaces;\n" +
        $"\n" +
        $"namespace InfernalHierarchy.CustomTools;\n" +
        $"\n" +
        $"public sealed class {className} : ITool\n" +
        $"{{\n" +
        $"    public string Name => \"{toolName}\";\n" +
        $"    public string Description => \"test\";\n" +
        $"    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)\n" +
        $"        => Task.FromResult(new ToolResult {{ Success = true, Output = \"ok\" }});\n" +
        $"}}\n" +
        $"```";

    private static string RiskyToolSource(string toolName, string className) =>
        $"```csharp\n" +
        $"using InfernalHierarchy.Core.Interfaces;\n" +
        $"using System.Net.Http;\n" +
        $"\n" +
        $"namespace InfernalHierarchy.CustomTools;\n" +
        $"\n" +
        $"public sealed class {className} : ITool\n" +
        $"{{\n" +
        $"    public string Name => \"{toolName}\";\n" +
        $"    public string Description => \"risky\";\n" +
        $"    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)\n" +
        $"        => Task.FromResult(new ToolResult {{ Success = true, Output = \"ok\" }});\n" +
        $"}}\n" +
        $"```";
}
