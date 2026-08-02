using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Tools.Meta;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class CustomToolManagementToolsTests
{
    [Fact]
    public async Task ListCustomTools_ShouldReturnPersistedToolEntries()
    {
        var store = new InMemoryCustomToolStore();
        await store.UpsertAsync(new CustomToolDefinition
        {
            Id = "tool-1",
            ToolName = "custom_echo",
            Description = "Echo",
            SourceCode = "// source"
        });

        var registry = new InMemoryToolRegistry();
        registry.RegisterTool(new DummyTool("custom_echo"));

        var tool = new ListCustomToolsTool(store, registry);
        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("custom_echo");
        result.Output.Should().Contain("is_loaded");
    }

    [Fact]
    public async Task DeleteCustomTool_ShouldDeleteAndUnregister()
    {
        var store = new InMemoryCustomToolStore();
        await store.UpsertAsync(new CustomToolDefinition
        {
            Id = "tool-2",
            ToolName = "custom_remove_me",
            Description = "Removable",
            SourceCode = "// source"
        });

        var registry = new InMemoryToolRegistry();
        registry.RegisterTool(new DummyTool("custom_remove_me"));

        var tool = new DeleteCustomToolTool(store, registry);
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["tool_name"] = "custom_remove_me"
        });

        result.Success.Should().BeTrue();
        (await store.GetByNameAsync("custom_remove_me")).Should().BeNull();
        registry.GetTool("custom_remove_me").Should().BeNull();
    }

    private sealed class DummyTool : ITool
    {
        public DummyTool(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Description => "dummy";

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Success = true, Output = "ok" });
    }

    private sealed class InMemoryToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterTool(ITool tool) => _tools[tool.Name] = tool;

        public bool UnregisterTool(string name) => _tools.Remove(name);

        public ITool? GetTool(string name) => _tools.TryGetValue(name, out var tool) ? tool : null;

        public IEnumerable<ITool> GetAllTools() => _tools.Values;

        public IEnumerable<ITool> GetToolsForAgent(string[] toolNames)
            => toolNames.Select(GetTool).Where(t => t is not null)!;

        public Task<ToolResult> ExecuteToolWithTrackingAsync(
            string toolName,
            Dictionary<string, object> parameters,
            string? agentId = null,
            string? agentRank = null,
            string? agentName = null,
            CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Success = false, Error = "not implemented" });
    }

    private sealed class InMemoryCustomToolStore : ICustomToolStore
    {
        private readonly Dictionary<string, CustomToolDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);

        public Task UpsertAsync(CustomToolDefinition tool, CancellationToken ct = default)
        {
            _byId[tool.Id] = tool;
            return Task.CompletedTask;
        }

        public Task<CustomToolDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_byId.TryGetValue(id, out var tool) ? tool : null);

        public Task<CustomToolDefinition?> GetByNameAsync(string toolName, CancellationToken ct = default)
            => Task.FromResult(_byId.Values.FirstOrDefault(t => string.Equals(t.ToolName, toolName, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<CustomToolDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CustomToolDefinition>>(_byId.Values.ToList());

        public Task<bool> DeleteByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_byId.Remove(id));

        public Task<bool> DeleteByNameAsync(string toolName, CancellationToken ct = default)
        {
            var found = _byId.Values.FirstOrDefault(t => string.Equals(t.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(found is not null && _byId.Remove(found.Id));
        }
    }
}