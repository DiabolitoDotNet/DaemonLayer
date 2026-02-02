using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public class AgentFactoryTests
{
    private readonly Mock<IPersonaLoader> _mockPersonaLoader;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ISharedMemory> _mockSharedMemory;
    private readonly Mock<IToolRegistry> _mockToolRegistry;
    private readonly Mock<AgentRegistry> _mockRegistry;
    private readonly OllamaClient _ollamaClient;
    private readonly Mock<ILogger<AgentFactory>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly AgentFactory _factory;

    public AgentFactoryTests()
    {
        _mockPersonaLoader = new Mock<IPersonaLoader>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockSharedMemory = new Mock<ISharedMemory>();
        _mockToolRegistry = new Mock<IToolRegistry>();
        _mockRegistry = new Mock<AgentRegistry>(Mock.Of<ILogger<AgentRegistry>>());

        // Create a real OllamaClient with mock dependencies
        var ollamaOptions = Microsoft.Extensions.Options.Options.Create(new InfernalHierarchy.Tools.OllamaOptions());
        var ollamaLogger = Mock.Of<ILogger<OllamaClient>>();
        _ollamaClient = new OllamaClient(ollamaOptions, ollamaLogger);

        _mockLogger = new Mock<ILogger<AgentFactory>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();

        _factory = new AgentFactory(
            _mockPersonaLoader.Object,
            _mockMessageBus.Object,
            _mockSharedMemory.Object,
            _mockToolRegistry.Object,
            _mockRegistry.Object,
            _ollamaClient,
            _mockLogger.Object,
            _mockLoggerFactory.Object
        );
    }

    [Fact]
    public async Task CreateAgentAsync_ShouldThrowException_WhenPersonaNotFound()
    {
        // Arrange
        _mockPersonaLoader.Setup(x => x.LoadPersonaAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Persona?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _factory.CreateAgentAsync("nonexistent", AgentRank.Duke));
    }

    [Fact]
    public async Task CreateAgentAsync_ShouldCreateAgent_WhenPersonaExists()
    {
        // Arrange
        var persona = new Persona
        {
            Name = "Vassago",
            DemonTitle = "The Revealer",
            SystemPrompt = "You are Vassago",
            AvailableTools = new[] { "web_search" }
        };

        _mockPersonaLoader.Setup(x => x.LoadPersonaAsync("Vassago", It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        // Act
        var agent = await _factory.CreateAgentAsync("Vassago", AgentRank.Duke);

        // Assert
        agent.Should().NotBeNull();
        agent.Name.Should().Be("Vassago");
        agent.Rank.Should().Be(AgentRank.Duke);
    }
}
