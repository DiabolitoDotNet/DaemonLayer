using InfernalHierarchy.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

/// <summary>
/// Tests for AgentLearningService - tool performance tracking and agent learning.
/// </summary>
public class AgentLearningTests
{
    private readonly AgentLearningService _learningService;
    private readonly Mock<ILogger<AgentLearningService>> _mockLogger;

    public AgentLearningTests()
    {
        _mockLogger = new Mock<ILogger<AgentLearningService>>();
        _learningService = new AgentLearningService(_mockLogger.Object);
    }

    [Fact]
    public void RecordToolExecution_Success_TracksSuccessRate()
    {
        // Arrange
        const string agentId = "vassago";
        const string toolName = "web_search";
        const string agentRank = "Duke";

        // Act - Record 8 successful and 2 failed executions
        for (int i = 0; i < 8; i++)
        {
            _learningService.RecordToolExecution(agentId, agentRank, toolName, success: true, TimeSpan.FromSeconds(1));
        }

        for (int i = 0; i < 2; i++)
        {
            _learningService.RecordToolExecution(agentId, agentRank, toolName, success: false, TimeSpan.FromSeconds(2));
        }

        // Assert
        var successRate = _learningService.GetToolSuccessRate(toolName);
        Assert.Equal(0.8, successRate, precision: 2); // 80% success rate

        var agentProficiency = _learningService.GetAgentToolProficiency(agentId, toolName);
        Assert.Equal(0.8, agentProficiency, precision: 2); // Agent also 80% proficient
    }

    [Fact]
    public void RecordToolExecution_Failure_IncrementsFailureCount()
    {
        // Arrange
        const string agentId = "baal";
        const string toolName = "create_sub_agent";
        const string agentRank = "Prince";

        // Act - Record failures only
        for (int i = 0; i < 5; i++)
        {
            _learningService.RecordToolExecution(agentId, agentRank, toolName, success: false, TimeSpan.FromSeconds(3));
        }

        // Assert
        var successRate = _learningService.GetToolSuccessRate(toolName);
        Assert.Equal(0.0, successRate); // 0% success rate

        var agentProficiency = _learningService.GetAgentToolProficiency(agentId, toolName);
        Assert.Equal(0.0, agentProficiency); // 0% proficiency
    }

    [Fact]
    public void GetSystemStats_ReturnsAggregatedMetrics()
    {
        // Arrange
        _learningService.RecordToolExecution("agent1", "Duke", "web_search", true, TimeSpan.FromSeconds(1));
        _learningService.RecordToolExecution("agent1", "Duke", "read_memory", true, TimeSpan.FromSeconds(0.5));
        _learningService.RecordToolExecution("agent2", "Worker", "write_memory", false, TimeSpan.FromSeconds(2));
        _learningService.RecordToolExecution("agent2", "Worker", "web_search", true, TimeSpan.FromSeconds(1.5));

        // Act
        var stats = _learningService.GetSystemStats();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(2, stats.TotalAgentsTracked); // agent1, agent2
        Assert.Equal(3, stats.TotalToolsTracked); // web_search, read_memory, write_memory
    }

    [Theory]
    [InlineData("Supreme", "send_telegram", 10, 9, 0.9)]
    [InlineData("Prince", "create_sub_agent", 20, 16, 0.8)]
    [InlineData("Duke", "web_search", 15, 14, 0.933)]
    [InlineData("Worker", "read_memory", 5, 5, 1.0)]
    public void RecordToolExecution_VariousRanks_TracksCorrectly(
        string rank,
        string toolName,
        int successCount,
        int actualSuccess,
        double expectedRate)
    {
        // Arrange
        var agentId = $"agent_{rank.ToUpperInvariant()}";
        var failureCount = successCount - actualSuccess;

        // Act
        for (int i = 0; i < actualSuccess; i++)
        {
            _learningService.RecordToolExecution(agentId, rank, toolName, success: true, TimeSpan.FromMilliseconds(500));
        }

        for (int i = 0; i < failureCount; i++)
        {
            _learningService.RecordToolExecution(agentId, rank, toolName, success: false, TimeSpan.FromMilliseconds(1000));
        }

        // Assert
        var successRate = _learningService.GetToolSuccessRate(toolName);
        Assert.Equal(expectedRate, successRate, precision: 2);
    }

    [Fact]
    public void GetToolSuccessRate_NoExecutions_ReturnsZero()
    {
        // Act
        var successRate = _learningService.GetToolSuccessRate("nonexistent_tool");

        // Assert
        Assert.Equal(0.0, successRate);
    }
}
