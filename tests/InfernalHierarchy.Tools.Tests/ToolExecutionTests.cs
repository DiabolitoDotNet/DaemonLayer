using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

/// <summary>
/// Unit tests for individual tool executions covering happy paths, error handling, and edge cases
/// </summary>
public class ToolExecutionTests
{
    [Fact]
    public async Task MemoryWriteTool_AddFact_StoresSuccessfully()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryWriteTool>>();

        mockMemory.Setup(x => x.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new MemoryWriteTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "content", "Test fact content" },
            { "category", "test" },
            { "source", "unit_test" },
            { "confidence", 1.0 }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        mockMemory.Verify(x => x.AddFactAsync(
            It.Is<Fact>(f => f.Content == "Test fact content" && f.Category == "test"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MemoryReadTool_SearchFacts_ReturnsResults()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        var facts = new List<Fact>
        {
            new Fact
            {
                Id = Guid.NewGuid().ToString(),
                Category = "test",
                Content = "Test fact 1",
                Source = "test",
                CreatedBy = "agent1"
            },
            new Fact
            {
                Id = Guid.NewGuid().ToString(),
                Category = "test",
                Content = "Test fact 2",
                Source = "test",
                CreatedBy = "agent1"
            }
        };

        mockMemory.Setup(x => x.SearchFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "query", "test" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Test fact 1", result.Output);
        Assert.Contains("Test fact 2", result.Output);
    }

    [Fact]
    public async Task CreateSubAgentTool_WithValidParameters_CreatesAgent()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();

        var mockAgent = new Mock<IAgent>();
        mockAgent.SetupGet(x => x.Id).Returns("new_agent_id");
        mockAgent.SetupGet(x => x.Name).Returns("TestAgent");

        mockFactory.Setup(x => x.CreateAgentAsync(
            It.IsAny<string>(),
            It.IsAny<AgentRank>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAgent.Object);

        var tool = new CreateSubAgentTool(mockFactory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "name", "TestAgent" },
            { "rank", "Duke" },
            { "parent_agent_id", "parent1" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("new_agent_id", result.Output);
    }

    [Fact]
    public async Task ToolExecution_WithMissingRequiredParameter_ReturnsError()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryWriteTool>>();
        var tool = new MemoryWriteTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            // Missing required "content" parameter
            { "category", "test" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task ToolExecution_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.SearchFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string query, CancellationToken ct) =>
            {
                await Task.Delay(10000, ct); // Long delay
                return new List<Fact>();
            });

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        using var cts = new CancellationTokenSource(100); // 100ms timeout

        var parameters = new Dictionary<string, object> { { "query", "test" } };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await tool.ExecuteAsync(parameters, cts.Token);
        });
    }

    [Fact]
    public async Task ToolExecution_ConcurrentCalls_HandlesCorrectly()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.SearchFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Fact>
            {
                new Fact
                {
                    Id = Guid.NewGuid().ToString(),
                    Category = "test",
                    Content = "Concurrent test",
                    Source = "test",
                    CreatedBy = "agent1"
                }
            });

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);
        var parameters = new Dictionary<string, object> { { "query", "test" } };

        // Act
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            tool.ExecuteAsync(parameters, CancellationToken.None));

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(10, results.Length);
    }

    [Fact]
    public async Task MemoryWriteTool_AddDecision_StoresWithReasoning()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryWriteTool>>();

        mockMemory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new MemoryWriteTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "decision" },
            { "context", "Test context" },
            { "action", "Test action" },
            { "reasoning", "Test reasoning" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        mockMemory.Verify(x => x.AddDecisionAsync(
            It.Is<Decision>(d => d.Action == "Test action" && d.Reasoning == "Test reasoning"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MemoryReadTool_GetFactsByCategory_FiltersCorrectly()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        var facts = new List<Fact>
        {
            new Fact { Id = "1", Category = "technical", Content = "Technical fact", Source = "test", CreatedBy = "agent1" },
            new Fact { Id = "2", Category = "technical", Content = "Another technical fact", Source = "test", CreatedBy = "agent1" }
        };

        mockMemory.Setup(x => x.GetFactsByCategoryAsync("technical", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "category", "technical" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Technical fact", result.Output);
    }

    [Fact]
    public async Task ToolRegistry_GetTool_ReturnsTool()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ToolRegistry>>();
        var registry = new ToolRegistry(mockLogger.Object);
        var mockTool = new Mock<ITool>();
        mockTool.SetupGet(x => x.Name).Returns("test_tool");
        mockTool.SetupGet(x => x.Description).Returns("A test tool");

        registry.RegisterTool(mockTool.Object);

        // Act
        var tool = registry.GetTool("test_tool");

        // Assert
        Assert.NotNull(tool);
        Assert.Equal("test_tool", tool.Name);
    }

    [Fact]
    public void ToolRegistry_GetNonExistentTool_ThrowsException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ToolRegistry>>();
        var registry = new ToolRegistry(mockLogger.Object);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => registry.GetTool("non_existent_tool"));
    }
}
