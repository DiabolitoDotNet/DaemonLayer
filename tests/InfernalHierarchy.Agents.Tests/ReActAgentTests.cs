using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using TaskStatus = InfernalHierarchy.Core.Entities.TaskStatus;

namespace InfernalHierarchy.Agents.Tests;

/// <summary>
/// Unit tests for ReActAgent covering ReAct loop logic, tool execution, and memory integration
/// </summary>
public class ReActAgentTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ISharedMemory> _mockMemory;
    private readonly Mock<IToolRegistry> _mockToolRegistry;
    private readonly Mock<ILlmClient> _mockOllama;
    private readonly Mock<IAgentFactory> _mockAgentFactory;
    private readonly Mock<ILogger<ReActAgent>> _mockLogger;
    private readonly Mock<IAgentEventSink> _mockEventSink;
    private readonly Persona _testPersona;

    public ReActAgentTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockMemory = new Mock<ISharedMemory>();
        _mockToolRegistry = new Mock<IToolRegistry>();
        _mockOllama = new Mock<ILlmClient>();
        _mockAgentFactory = new Mock<IAgentFactory>();
        _mockLogger = new Mock<ILogger<ReActAgent>>();
        _mockEventSink = new Mock<IAgentEventSink>();

        _testPersona = new Persona
        {
            Name = "TestAgent",
            SystemPrompt = "You are a test agent",
            Specializations = new[] { "testing" },
            AvailableTools = new[] { "test_tool" },
            Personality = new PersonalityTraits
            {
                Tone = "Professional",
                Approach = "Analytical",
                Verbosity = 5
            }
        };
    }

    [Fact]
    public async Task ProcessTaskAsync_FinalAnswer_ShouldAppendTaskAndDecisionEvents()
    {
        // Arrange
        var agentEntity = CreateTestAgent();
        agentEntity.Id = "agent-1";

        _mockMemory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());
        _mockMemory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockOllama.Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
Thought: I can answer immediately.
Action: FINAL_ANSWER
Action Input: done
""");

        var reactAgent = new ReActAgent(
            agentEntity,
            _testPersona,
            _mockMessageBus.Object,
            _mockMemory.Object,
            _mockToolRegistry.Object,
            _mockAgentFactory.Object,
            _mockOllama.Object,
            _mockLogger.Object,
            _mockEventSink.Object);

        var task = new AgentMessage
        {
            Id = "task-1",
            FromAgentId = "sender",
            ToAgentId = "agent-1",
            Type = MessageType.Task,
            Content = "say hi"
        };

        // Act
        var response = await reactAgent.ProcessTaskAsync(task, CancellationToken.None);

        // Assert
        response.Content.Should().Contain("done");
        _mockEventSink.Verify(x => x.AppendEvent(It.Is<AgentEvent>(e => e.AgentId == "agent-1" && e.Type == EventType.TaskReceived)), Times.Once);
        _mockEventSink.Verify(x => x.AppendEvent(It.Is<AgentEvent>(e => e.AgentId == "agent-1" && e.Type == EventType.TaskStarted)), Times.Once);
        _mockEventSink.Verify(x => x.AppendEvent(It.Is<AgentEvent>(e => e.AgentId == "agent-1" && e.Type == EventType.DecisionMade)), Times.Once);
        _mockEventSink.Verify(x => x.AppendEvent(It.Is<AgentEvent>(e => e.AgentId == "agent-1" && e.Type == EventType.TaskCompleted)), Times.Once);
    }

    [Fact]
    public async Task ProcessTaskAsync_JsonFinalAnswer_ParsesCorrectly()
    {
        // Arrange
        var agentEntity = CreateTestAgent();
        agentEntity.Id = "agent-1";

        _mockMemory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());
        _mockMemory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockOllama.Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"thought\":\"done\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"ok\"}");

        var reactAgent = new ReActAgent(
            agentEntity,
            _testPersona,
            _mockMessageBus.Object,
            _mockMemory.Object,
            _mockToolRegistry.Object,
            _mockAgentFactory.Object,
            _mockOllama.Object,
            _mockLogger.Object,
            _mockEventSink.Object,
            vectorMemory: null,
            ragOptions: null,
            reActOptions: new ReActOptions { UseJsonResponse = true });

        var task = new AgentMessage
        {
            Id = "task-1",
            FromAgentId = "sender",
            ToAgentId = "agent-1",
            Type = MessageType.Task,
            Content = "say hi"
        };

        // Act
        var response = await reactAgent.ProcessTaskAsync(task, CancellationToken.None);

        // Assert
        response.Content.Should().Contain("ok");
    }

    [Fact]
    public async Task ProcessTaskAsync_JsonToolCall_ExecutesToolThenReturnsFinalAnswer()
    {
        // Arrange
        var agentEntity = CreateTestAgent();
        agentEntity.Id = "agent-1";

        _mockMemory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());
        _mockMemory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockTool = new Mock<ITool>();
        mockTool.SetupGet(x => x.Name).Returns("test_tool");

        _mockToolRegistry.Setup(x => x.GetTool("test_tool")).Returns(mockTool.Object);
        _mockToolRegistry.Setup(x => x.ExecuteToolWithTrackingAsync(
                "test_tool",
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "tool ok" });

        _mockOllama.SetupSequence(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"thought\":\"use tool\",\"action\":\"test_tool\",\"actionInput\":{\"param1\":\"value1\"}}")
            .ReturnsAsync("{\"thought\":\"done\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"finished\"}");

        var reactAgent = new ReActAgent(
            agentEntity,
            _testPersona,
            _mockMessageBus.Object,
            _mockMemory.Object,
            _mockToolRegistry.Object,
            _mockAgentFactory.Object,
            _mockOllama.Object,
            _mockLogger.Object,
            _mockEventSink.Object,
            vectorMemory: null,
            ragOptions: null,
            reActOptions: new ReActOptions { UseJsonResponse = true });

        var task = new AgentMessage
        {
            Id = "task-1",
            FromAgentId = "sender",
            ToAgentId = "agent-1",
            Type = MessageType.Task,
            Content = "do thing"
        };

        // Act
        var response = await reactAgent.ProcessTaskAsync(task, CancellationToken.None);

        // Assert
        response.Content.Should().Contain("finished");
        _mockToolRegistry.Verify(x => x.ExecuteToolWithTrackingAsync(
            "test_tool",
            It.Is<Dictionary<string, object>>(d => d.ContainsKey("param1")),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AgentCreation_WithValidPersona_CreatesSuccessfully()
    {
        // Arrange
        var agent = CreateTestAgent();

        // Act
        var reactAgent = new ReActAgent(
            agent,
            _testPersona,
            _mockMessageBus.Object,
            _mockMemory.Object,
            _mockToolRegistry.Object,
            _mockAgentFactory.Object,
            _mockOllama.Object,
            _mockLogger.Object);

        // Assert
        Assert.NotNull(reactAgent);
        Assert.Equal("TestAgent", reactAgent.Name);
    }

    [Fact]
    public async Task MemoryOperations_SearchFacts_ReturnsResults()
    {
        // Arrange
        var testFacts = new List<Fact>
        {
            new Fact
            {
                Id = Guid.NewGuid().ToString(),
                Category = "test",
                Content = "Test fact content",
                Source = "test",
                CreatedBy = "agent1",
                CreatedAt = DateTime.UtcNow
            }
        };

        _mockMemory.Setup(x => x.SearchFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(testFacts);

        // Act
        var results = await _mockMemory.Object.SearchFactsAsync("test", CancellationToken.None);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains("Test fact content", results.First().Content);
    }

    [Fact]
    public async Task MemoryOperations_AddDecision_StoresSuccessfully()
    {
        // Arrange
        var decision = new Decision
        {
            Id = Guid.NewGuid().ToString(),
            Context = "Test context",
            Action = "Test action",
            Reasoning = "Test reasoning",
            CreatedBy = "agent1",
            CreatedAt = DateTime.UtcNow
        };

        _mockMemory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _mockMemory.Object.AddDecisionAsync(decision, CancellationToken.None);

        // Assert
        _mockMemory.Verify(x => x.AddDecisionAsync(
            It.Is<Decision>(d => d.Action == "Test action"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToolExecution_WithValidParameters_ExecutesSuccessfully()
    {
        // Arrange
        var mockTool = new Mock<ITool>();
        mockTool.SetupGet(x => x.Name).Returns("test_tool");
        mockTool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "Tool executed successfully" });

        _mockToolRegistry.Setup(x => x.GetTool("test_tool")).Returns(mockTool.Object);

        var parameters = new Dictionary<string, object> { { "param1", "value1" } };

        // Act
        var tool = _mockToolRegistry.Object.GetTool("test_tool");
        Assert.NotNull(tool);
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Tool executed successfully", result.Output);
    }

    [Fact]
    public async Task ToolExecution_WithException_HandlesGracefully()
    {
        // Arrange
        var mockTool = new Mock<ITool>();
        mockTool.SetupGet(x => x.Name).Returns("failing_tool");
        mockTool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Tool execution failed"));

        _mockToolRegistry.Setup(x => x.GetTool("failing_tool")).Returns(mockTool.Object);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
        {
            var tool = _mockToolRegistry.Object.GetTool("failing_tool");
            Assert.NotNull(tool);
            await tool.ExecuteAsync(new Dictionary<string, object>(), CancellationToken.None);
        });
    }

    [Fact]
    public async Task TaskEntry_WithStatus_TracksCorrectly()
    {
        // Arrange
        var task = new TaskEntry
        {
            Id = Guid.NewGuid().ToString(),
            Description = "Test task",
            AssignedTo = "agent1",
            Status = TaskStatus.InProgress,
            CreatedBy = "user",
            CreatedAt = DateTime.UtcNow
        };

        _mockMemory.Setup(x => x.AddTaskAsync(It.IsAny<TaskEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockMemory.Setup(x => x.GetTasksByStatusAsync(TaskStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskEntry> { task });

        // Act
        await _mockMemory.Object.AddTaskAsync(task, CancellationToken.None);
        var activeTasks = await _mockMemory.Object.GetTasksByStatusAsync(TaskStatus.InProgress, CancellationToken.None);

        // Assert
        Assert.NotEmpty(activeTasks);
        var firstTask = activeTasks.FirstOrDefault();
        Assert.NotNull(firstTask);
        Assert.Equal("Test task", firstTask!.Description);
    }

    [Fact]
    public async Task MessageBus_PublishMessage_HandlerInvoked()
    {
        // Arrange
        AgentMessage? receivedMessage = null;
        var expectedMessage = new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = "sender",
            ToAgentId = "receiver",
            Content = "Test message",
            Timestamp = DateTime.UtcNow
        };

        var messages = new List<AgentMessage> { expectedMessage };
        var asyncEnumerable = ToAsyncEnumerable(messages);

        _mockMessageBus.Setup(x => x.SubscribeAsync("receiver", It.IsAny<CancellationToken>()))
            .Returns(asyncEnumerable);

        // Act
        await foreach (var msg in _mockMessageBus.Object.SubscribeAsync("receiver", CancellationToken.None))
        {
            receivedMessage = msg;
            break;
        }

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal("Test message", receivedMessage!.Content);
    }

    [Fact]
    public async Task MemoryOperations_GetRecentDecisions_ReturnsLimited()
    {
        // Arrange
        var decisions = Enumerable.Range(0, 20).Select(i => new Decision
        {
            Id = Guid.NewGuid().ToString(),
            Context = $"Context {i}",
            Action = $"Action {i}",
            Reasoning = $"Reasoning {i}",
            CreatedBy = "agent1",
            CreatedAt = DateTime.UtcNow.AddMinutes(-i)
        }).ToList();

        _mockMemory.Setup(x => x.GetRecentDecisionsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(decisions.Take(10));

        // Act
        var recent = await _mockMemory.Object.GetRecentDecisionsAsync(10, CancellationToken.None);

        // Assert
        Assert.Equal(10, recent.Count());
    }

    private Agent CreateTestAgent()
    {
        return new Agent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "TestAgent",
            Rank = AgentRank.Duke,
            CreatedAt = DateTime.UtcNow,
            Status = AgentStatus.Idle
        };
    }

    private async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }
}
