using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Marketplace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Tools.Tests.Marketplace;

public sealed class ToolPluginDiscoveryTests
{
    private sealed class PluginTool : ITool
    {
        public string Name => "plugin_tool";
        public string Description => "d";
        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Success = true, Output = "ok" });
    }

    [Fact]
    public void DiscoverToolTypes_FindsToolImplementations()
    {
        var types = ToolPluginDiscovery.DiscoverToolTypes(typeof(PluginTool).Assembly);

        types.Should().Contain(typeof(PluginTool));
    }

    [Fact]
    public void CreateTools_CreatesInstances()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var tools = ToolPluginDiscovery.CreateTools(typeof(PluginTool).Assembly, services, NullLogger.Instance);

        tools.Should().ContainSingle(t => t.Name == "plugin_tool");
    }
}
