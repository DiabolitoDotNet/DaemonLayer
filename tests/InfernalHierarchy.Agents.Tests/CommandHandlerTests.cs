using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Personas;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

/// <summary>
/// Tests for command handling in ReActAgent (e.g., /usage, /models commands).
/// </summary>
public class CommandHandlerTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ISharedMemory> _mockSharedMemory;
    private readonly Mock<IToolRegistry> _mockToolRegistry;
    private readonly Mock<IAgentFactory> _mockAgentFactory;
    private readonly Mock<ILlmClient> _mockOllamaClient;
    private readonly Mock<ILogger<ReActAgent>> _mockLogger;

    public CommandHandlerTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockSharedMemory = new Mock<ISharedMemory>();
        _mockToolRegistry = new Mock<IToolRegistry>();
        _mockAgentFactory = new Mock<IAgentFactory>();
        _mockOllamaClient = new Mock<ILlmClient>();
        _mockLogger = new Mock<ILogger<ReActAgent>>();
    }

    [Fact]
    public async Task ProcessTaskAsync_NonCommandMessage_UsesReActLoop()
    {
        // Arrange
        var agent = CreateTestAgent();
        var message = new AgentMessage
        {
            FromAgentId = "user_789",
            ToAgentId = "lucifer",
            Content = "What is the weather like?",
            Payload = new Dictionary<string, object>()
        };

        // Mock Ollama to return a simple response (to test it goes through ReAct loop)
        _mockOllamaClient
            .Setup(o => o.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("I don't have real-time weather information, but I can help with other tasks.");

        // Act
        await agent.ProcessTaskAsync(message, CancellationToken.None);

        // Assert - Should call Ollama (ReAct loop), not immediate tool execution
        _mockOllamaClient.Verify(
            o => o.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    private ReActAgent CreateTestAgent()
    {
        var agentEntity = new Agent
        {
            Id = "lucifer",
            Name = "Lucifer",
            Rank = AgentRank.Supreme,
            Status = AgentStatus.Idle
        };

        var persona = new Persona
        {
            Name = "Lucifer",
            SystemPrompt = "You are Lucifer, the Supreme Commander.",
            Specializations = new List<string> { "command", "orchestration" },
            AvailableTools = new List<string> { "telegram_send", "read_memory", "write_memory" }
        };

        return new ReActAgent(
            agentEntity,
            persona,
            _mockMessageBus.Object,
            _mockSharedMemory.Object,
            _mockToolRegistry.Object,
            _mockAgentFactory.Object,
            _mockOllamaClient.Object,
            _mockLogger.Object);
    }
}
