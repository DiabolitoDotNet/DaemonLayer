using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public class ToolRegistryTests
{
    private readonly ToolRegistry _registry;

    public ToolRegistryTests()
    {
        var logger = Mock.Of<ILogger<ToolRegistry>>();
        _registry = new ToolRegistry(logger);
    }

    [Fact]
    public void RegisterTool_ShouldAddToolToRegistry()
    {
        // Arrange
        var mockTool = new Mock<ITool>();
        mockTool.Setup(x => x.Name).Returns("test_tool");
        mockTool.Setup(x => x.Description).Returns("A test tool");

        // Act
        _registry.RegisterTool(mockTool.Object);
        var retrieved = _registry.GetTool("test_tool");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("test_tool");
    }

    [Fact]
    public void GetTool_ShouldBeCaseInsensitive()
    {
        // Arrange
        var mockTool = new Mock<ITool>();
        mockTool.Setup(x => x.Name).Returns("TestTool");

        _registry.RegisterTool(mockTool.Object);

        // Act
        var retrieved = _registry.GetTool("testtool");

        // Assert
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public void GetTool_ShouldReturnNull_WhenToolNotFound()
    {
        // Act
        var retrieved = _registry.GetTool("nonexistent");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public void GetAllTools_ShouldReturnAllRegisteredTools()
    {
        // Arrange
        var tool1 = CreateMockTool("tool1");
        var tool2 = CreateMockTool("tool2");
        var tool3 = CreateMockTool("tool3");

        _registry.RegisterTool(tool1);
        _registry.RegisterTool(tool2);
        _registry.RegisterTool(tool3);

        // Act
        var allTools = _registry.GetAllTools();

        // Assert
        allTools.Should().HaveCount(3);
    }

    [Fact]
    public void GetToolsForAgent_ShouldReturnOnlyRequestedTools()
    {
        // Arrange
        _registry.RegisterTool(CreateMockTool("web_search"));
        _registry.RegisterTool(CreateMockTool("read_memory"));
        _registry.RegisterTool(CreateMockTool("write_memory"));
        _registry.RegisterTool(CreateMockTool("send_telegram"));

        // Act
        var agentTools = _registry.GetToolsForAgent(new[] { "web_search", "read_memory" });

        // Assert
        agentTools.Should().HaveCount(2);
        agentTools.Select(t => t.Name).Should().Contain("web_search");
        agentTools.Select(t => t.Name).Should().Contain("read_memory");
    }

    private ITool CreateMockTool(string name)
    {
        var mock = new Mock<ITool>();
        mock.Setup(x => x.Name).Returns(name);
        mock.Setup(x => x.Description).Returns($"Description for {name}");
        return mock.Object;
    }
}
