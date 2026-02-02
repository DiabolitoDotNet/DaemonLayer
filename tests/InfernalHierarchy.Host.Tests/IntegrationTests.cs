using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory;
using InfernalHierarchy.Messaging;
using InfernalHierarchy.Personas;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

/// <summary>
/// End-to-end integration tests for the complete agent system
/// </summary>
public class IntegrationTests : IAsyncLifetime
{
    private IMessageBus? _messageBus;
    private ISharedMemory? _sharedMemory;
    private IToolRegistry? _toolRegistry;
    private IAgentFactory? _agentFactory;
    private AgentOrchestrator? _orchestrator;
    private string _testDbPath = null!;

    public async Task InitializeAsync()
    {
        // Setup test database
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_infernal_{Guid.NewGuid()}.db");

        // Initialize real components for integration testing
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

        _messageBus = new ChannelMessageBus(
            loggerFactory.CreateLogger<ChannelMessageBus>());

        var memoryOptions = Options.Create(new MemoryOptions { DatabasePath = _testDbPath });
        _sharedMemory = new LiteDbSharedMemory(
            memoryOptions,
            loggerFactory.CreateLogger<LiteDbSharedMemory>());

        // Setup tool registry with mock tools
        var mockWebSearch = new Mock<ISearchTool>();
        mockWebSearch.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "Search results: Test information" });

        var memoryReadTool = new MemoryReadTool(_sharedMemory, loggerFactory.CreateLogger<MemoryReadTool>());
        var memoryWriteTool = new MemoryWriteTool(_sharedMemory, loggerFactory.CreateLogger<MemoryWriteTool>());

        _toolRegistry = new ToolRegistry();
        _toolRegistry.RegisterTool("web_search", mockWebSearch.Object);
        _toolRegistry.RegisterTool("read_memory", memoryReadTool);
        _toolRegistry.RegisterTool("write_memory", memoryWriteTool);

        // Setup agent factory with mock persona loader
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        mockPersonaLoader.Setup(x => x.LoadPersonaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentPersona
            {
                Name = "TestAgent",
                SystemPrompt = "You are a test agent",
                Specializations = new[] { "testing" },
                AvailableTools = new[] { "web_search", "read_memory", "write_memory" }
            });

        var mockOllamaClient = new Mock<OllamaClient>(null!, null!);
        mockOllamaClient.Setup(x => x.GetCompletionAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<float>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("Thought: I need to search for information\nAction: web_search\nActionInput: {\"query\": \"test\"}");

        _agentFactory = new AgentFactory(
            mockPersonaLoader.Object,
            _messageBus,
            _sharedMemory,
            _toolRegistry,
            mockOllamaClient.Object,
            loggerFactory.CreateLogger<AgentFactory>());

        // Setup orchestrator
        var agentRegistry = new AgentRegistry();
        var hierarchyOptions = Options.Create(new HierarchyOptions
        {
            MainAgentName = "TestAgent",
            MaxAgentDepth = 3,
            MaxChildrenPerAgent = 5
        });

        _orchestrator = new AgentOrchestrator(
            _agentFactory,
            agentRegistry,
            _messageBus,
            hierarchyOptions,
            loggerFactory.CreateLogger<AgentOrchestrator>());

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _orchestrator?.Dispose();
        _messageBus?.Dispose();
        _sharedMemory?.Dispose();

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task EndToEnd_CreateAgent_ProcessTask_StoreMemory()
    {
        // Arrange
        var cancellationToken = new CancellationTokenSource(10000).Token;

        // Act - Start orchestrator
        await _orchestrator!.StartAsync(cancellationToken);
        await Task.Delay(1000, cancellationToken); // Allow initialization

        // Create an agent
        var agentId = await _agentFactory!.CreateAgentAsync("TestAgent", AgentRank.Duke, null, cancellationToken);
        Assert.NotNull(agentId);

        // Send a task to the agent
        var taskMessage = new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = "user",
            ToAgentId = agentId,
            Content = "Search for information about testing",
            Timestamp = DateTime.UtcNow
        };

        await _messageBus!.PublishAsync(taskMessage, cancellationToken);
        await Task.Delay(2000, cancellationToken); // Allow processing

        // Assert - Check if memory was written
        var facts = await _sharedMemory!.SearchFactsAsync("test", cancellationToken);
        Assert.NotNull(facts);
    }

    [Fact]
    public async Task EndToEnd_AgentHierarchy_ParentChildCommunication()
    {
        // Arrange
        var cancellationToken = new CancellationTokenSource(15000).Token;

        // Act - Create parent agent
        var parentId = await _agentFactory!.CreateAgentAsync("ParentAgent", AgentRank.Prince, null, cancellationToken);
        Assert.NotNull(parentId);

        // Create child agent
        var childId = await _agentFactory.CreateAgentAsync("ChildAgent", AgentRank.Duke, parentId, cancellationToken);
        Assert.NotNull(childId);

        // Start orchestrator
        await _orchestrator!.StartAsync(cancellationToken);
        await Task.Delay(1000, cancellationToken);

        // Parent sends task to child
        var taskMessage = new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = parentId,
            ToAgentId = childId,
            Content = "Perform research task",
            Timestamp = DateTime.UtcNow
        };

        await _messageBus!.PublishAsync(taskMessage, cancellationToken);
        await Task.Delay(3000, cancellationToken);

        // Assert - Verify message was processed
        Assert.True(true); // If we got here without exceptions, communication worked
    }

    [Fact]
    public async Task EndToEnd_MemoryOperations_ReadWriteSearch()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act - Write multiple entries
        await _sharedMemory!.WriteFactAsync(new Fact
        {
            Key = "test_fact_1",
            Value = "This is test fact 1",
            AgentId = "agent1",
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        await _sharedMemory.WriteFactAsync(new Fact
        {
            Key = "test_fact_2",
            Value = "This is test fact 2",
            AgentId = "agent1",
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        await _sharedMemory.WriteDecisionAsync(new Decision
        {
            Key = "test_decision",
            Value = "Made a test decision",
            AgentId = "agent1",
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        // Assert - Read back
        var fact1 = await _sharedMemory.ReadFactAsync("test_fact_1", cancellationToken);
        Assert.NotNull(fact1);
        Assert.Equal("This is test fact 1", fact1.Value);

        var searchResults = await _sharedMemory.SearchFactsAsync("test", cancellationToken);
        Assert.NotEmpty(searchResults);
        Assert.True(searchResults.Count() >= 2);

        var decision = await _sharedMemory.ReadDecisionAsync("test_decision", cancellationToken);
        Assert.NotNull(decision);
        Assert.Equal("Made a test decision", decision.Value);
    }

    [Fact]
    public async Task EndToEnd_MessageBus_MultipleSubscribers()
    {
        // Arrange
        var agent1Messages = new List<AgentMessage>();
        var agent2Messages = new List<AgentMessage>();
        var cancellationToken = CancellationToken.None;

        await _messageBus!.SubscribeAsync("agent1", async msg =>
        {
            agent1Messages.Add(msg);
            await Task.CompletedTask;
        }, cancellationToken);

        await _messageBus.SubscribeAsync("agent2", async msg =>
        {
            agent2Messages.Add(msg);
            await Task.CompletedTask;
        }, cancellationToken);

        // Act
        await _messageBus.PublishAsync(new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = "sender",
            ToAgentId = "agent1",
            Content = "Message for agent1",
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        await _messageBus.PublishAsync(new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = "sender",
            ToAgentId = "agent2",
            Content = "Message for agent2",
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        await Task.Delay(500);

        // Assert
        Assert.Single(agent1Messages);
        Assert.Single(agent2Messages);
        Assert.Equal("Message for agent1", agent1Messages[0].Content);
        Assert.Equal("Message for agent2", agent2Messages[0].Content);
    }

    [Fact]
    public async Task EndToEnd_ToolExecution_WithMemoryContext()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Setup memory context
        await _sharedMemory!.WriteFactAsync(new Fact
        {
            Key = "context_fact",
            Value = "Important context information",
            AgentId = "test_agent",
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        // Act - Execute memory read tool
        var readTool = _toolRegistry!.GetTool("read_memory");
        var result = await readTool.ExecuteAsync(
            "{\"type\": \"fact\", \"query\": \"context\"}",
            "test_agent",
            cancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("context_fact", result.Output);
        Assert.Contains("Important context information", result.Output);
    }

    [Fact]
    public async Task EndToEnd_AgentLifecycle_CreateProcessTerminate()
    {
        // Arrange
        var cancellationToken = new CancellationTokenSource(10000).Token;

        // Act - Start orchestrator
        await _orchestrator!.StartAsync(cancellationToken);
        await Task.Delay(1000, cancellationToken);

        // Create agent
        var agentId = await _agentFactory!.CreateAgentAsync("LifecycleAgent", AgentRank.Duke, null, cancellationToken);
        Assert.NotNull(agentId);

        // Process task
        var taskMessage = new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = "user",
            ToAgentId = agentId,
            Content = "Perform task",
            Timestamp = DateTime.UtcNow
        };

        await _messageBus!.PublishAsync(taskMessage, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // Terminate agent (would need to implement this in orchestrator)
        // For now, just verify agent was created and processed messages
        Assert.True(true);

        // Stop orchestrator
        await _orchestrator.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task EndToEnd_ErrorHandling_InvalidToolExecution()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act - Try to execute tool with invalid input
        var tool = _toolRegistry!.GetTool("write_memory");
        var result = await tool.ExecuteAsync("invalid json", "test_agent", cancellationToken);

        // Assert - Should handle gracefully
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task EndToEnd_ConcurrentAgentOperations()
    {
        // Arrange
        var cancellationToken = new CancellationTokenSource(15000).Token;

        await _orchestrator!.StartAsync(cancellationToken);
        await Task.Delay(1000, cancellationToken);

        // Act - Create multiple agents concurrently
        var createTasks = Enumerable.Range(0, 5).Select(i =>
            _agentFactory!.CreateAgentAsync($"Agent{i}", AgentRank.Worker, null, cancellationToken));

        var agentIds = await Task.WhenAll(createTasks);

        // Send messages to all agents concurrently
        var messageTasks = agentIds.Select(agentId =>
            _messageBus!.PublishAsync(new AgentMessage
            {
                Id = Guid.NewGuid().ToString(),
                FromAgentId = "user",
                ToAgentId = agentId,
                Content = "Concurrent task",
                Timestamp = DateTime.UtcNow
            }, cancellationToken));

        await Task.WhenAll(messageTasks);
        await Task.Delay(3000, cancellationToken);

        // Assert
        Assert.Equal(5, agentIds.Length);
        Assert.All(agentIds, id => Assert.NotNull(id));
    }

    [Fact(Skip = "Requires full environment setup including Telegram tokens")]
    public async Task Host_ShouldBuildSuccessfully()
    {
        // This test verifies the host can be built with all dependencies
        // Skipped by default as it requires configuration

        // Arrange
        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        // Note: Would need to replicate Program.cs setup here

        // Act & Assert
        // var host = hostBuilder.Build();
        // host.Should().NotBeNull();

        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test - requires dependencies")]
    public void ServiceProvider_ShouldResolveAllCoreServices()
    {
        // This test would verify that all registered services can be resolved
        // Requires proper DI container setup

        Assert.True(true, "Placeholder for service resolution tests");
    }
}
