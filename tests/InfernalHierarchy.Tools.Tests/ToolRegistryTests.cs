using FluentAssertions;
using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public class ToolRegistryTests
{
    private static readonly string[] WebAndReadMemoryToolNames = new[] { "web_search", "read_memory" };
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
        var agentTools = _registry.GetToolsForAgent(WebAndReadMemoryToolNames);

        // Assert
        agentTools.Should().HaveCount(2);
        agentTools.Select(t => t.Name).Should().Contain("web_search");
        agentTools.Select(t => t.Name).Should().Contain("read_memory");
    }

    private static ITool CreateMockTool(string name)
    {
        var mock = new Mock<ITool>();
        mock.Setup(x => x.Name).Returns(name);
        mock.Setup(x => x.Description).Returns($"Description for {name}");
        return mock.Object;
    }

    [Fact]
    public async Task ExecuteToolWithTrackingAsync_ShouldAppendEvent_OnSuccess()
    {
        var logger = Mock.Of<ILogger<ToolRegistry>>();
        var eventSink = new Mock<IAgentEventSink>();
        var registry = new ToolRegistry(logger, eventSink: eventSink.Object);

        var tool = new Mock<ITool>();
        tool.Setup(x => x.Name).Returns("test_tool");
        tool.Setup(x => x.Description).Returns("test");
        tool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "ok" });
        registry.RegisterTool(tool.Object);

        var result = await registry.ExecuteToolWithTrackingAsync(
            "test_tool",
            new Dictionary<string, object> { ["a"] = 1 },
            agentId: "agent-1",
            agentRank: "Duke",
            ct: CancellationToken.None);

        result.Success.Should().BeTrue();
        eventSink.Verify(x => x.AppendEvent(It.Is<AgentEvent>(e =>
            e.AgentId == "agent-1" && e.Type == EventType.ToolExecuted && (string)e.Metadata["tool"] == "test_tool")), Times.Once);
    }

    [Fact]
    public async Task ExecuteToolWithTrackingAsync_ShouldAppendErrorEvent_OnException()
    {
        var logger = Mock.Of<ILogger<ToolRegistry>>();
        var eventSink = new Mock<IAgentEventSink>();
        var registry = new ToolRegistry(logger, eventSink: eventSink.Object);

        var tool = new Mock<ITool>();
        tool.Setup(x => x.Name).Returns("boom");
        tool.Setup(x => x.Description).Returns("boom");
        tool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("nope"));
        registry.RegisterTool(tool.Object);

        var result = await registry.ExecuteToolWithTrackingAsync(
            "boom",
            new Dictionary<string, object>(),
            agentId: "agent-1",
            agentRank: "Duke",
            ct: CancellationToken.None);

        result.Success.Should().BeFalse();
        eventSink.Verify(x => x.AppendEvent(It.Is<AgentEvent>(e =>
            e.AgentId == "agent-1" && e.Type == EventType.ErrorOccurred && (string)e.Metadata["tool"] == "boom")), Times.Once);
    }

    [Fact]
    public async Task ExecuteToolWithTrackingAsync_ShouldReturnNotFound_WhenToolMissing()
    {
        var result = await _registry.ExecuteToolWithTrackingAsync(
            "missing_tool",
            new Dictionary<string, object>(),
            agentId: "agent-1",
            agentRank: "Worker",
            ct: CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Output.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteToolWithTrackingAsync_ShouldNotAppendEvent_WhenAgentIdMissing()
    {
        var logger = Mock.Of<ILogger<ToolRegistry>>();
        var eventSink = new Mock<IAgentEventSink>();
        var registry = new ToolRegistry(logger, eventSink: eventSink.Object);

        var tool = new Mock<ITool>();
        tool.Setup(x => x.Name).Returns("test_tool");
        tool.Setup(x => x.Description).Returns("test");
        tool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "ok" });
        registry.RegisterTool(tool.Object);

        var result = await registry.ExecuteToolWithTrackingAsync(
            "test_tool",
            new Dictionary<string, object> { ["a"] = 1 },
            agentId: null,
            agentRank: "Duke",
            ct: CancellationToken.None);

        result.Success.Should().BeTrue();
        eventSink.Verify(x => x.AppendEvent(It.IsAny<AgentEvent>()), Times.Never);
    }

    private sealed class CyclicNode
    {
        public CyclicNode? Next { get; set; }
        public override string ToString() => "CyclicNode";
    }

    [Fact]
    public async Task ExecuteToolWithTrackingAsync_ShouldFallbackSerializeParameters_WhenJsonSerializationFails()
    {
        var logger = Mock.Of<ILogger<ToolRegistry>>();
        var eventSink = new Mock<IAgentEventSink>();
        var registry = new ToolRegistry(logger, eventSink: eventSink.Object);

        var tool = new Mock<ITool>();
        tool.Setup(x => x.Name).Returns("test_tool");
        tool.Setup(x => x.Description).Returns("test");
        tool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "ok" });
        registry.RegisterTool(tool.Object);

        var node = new CyclicNode();
        node.Next = node;

        var result = await registry.ExecuteToolWithTrackingAsync(
            "test_tool",
            new Dictionary<string, object> { ["cycle"] = node },
            agentId: "agent-1",
            agentRank: "Worker",
            ct: CancellationToken.None);

        result.Success.Should().BeTrue();

        eventSink.Verify(x => x.AppendEvent(It.Is<AgentEvent>(e =>
            e.AgentId == "agent-1" &&
            e.Metadata.ContainsKey("parameters_json") &&
            e.Metadata["parameters_json"] != null &&
            e.Metadata["parameters_json"]!.ToString()!.Contains("cycle=")
        )), Times.Once);
    }

    private sealed class CountingExceptionHandler : GlobalExceptionHandler
    {
        public int Calls;

        public CountingExceptionHandler(ILogger<GlobalExceptionHandler> logger)
            : base(logger)
        {
        }

        protected override Task OnExceptionHandledAsync(Exception exception, ExceptionCategory category, string correlationId, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteToolWithTrackingAsync_WithGlobalExceptionHandler_ShouldInvokeHandlerInTryAndCatch()
    {
        var logger = Mock.Of<ILogger<ToolRegistry>>();
        var handlerLogger = Mock.Of<ILogger<GlobalExceptionHandler>>();
        var countingHandler = new CountingExceptionHandler(handlerLogger);

        var services = new ServiceCollection();
        services.AddSingleton<GlobalExceptionHandler>(countingHandler);
        var provider = services.BuildServiceProvider();

        var registry = new ToolRegistry(logger, serviceProvider: provider);

        var tool = new Mock<ITool>();
        tool.Setup(x => x.Name).Returns("boom");
        tool.Setup(x => x.Description).Returns("boom");
        tool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("nope"));
        registry.RegisterTool(tool.Object);

        var result = await registry.ExecuteToolWithTrackingAsync(
            "boom",
            new Dictionary<string, object>(),
            agentId: "agent-1",
            agentRank: "Worker",
            ct: CancellationToken.None);

        result.Success.Should().BeFalse();
        countingHandler.Calls.Should().Be(2);
    }
}
