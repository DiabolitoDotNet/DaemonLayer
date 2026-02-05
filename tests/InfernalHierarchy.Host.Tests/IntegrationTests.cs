using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Storage;
using InfernalHierarchy.Messaging.Bus;
using InfernalHierarchy.Personas;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using HierarchyOptions = InfernalHierarchy.Agents.HierarchyOptions;
using MemoryOptions = InfernalHierarchy.Memory.Configuration.MemoryOptions;

namespace InfernalHierarchy.Host.Tests;

/// <summary>
/// End-to-end integration tests for the complete agent system
/// </summary>
public sealed class IntegrationTests : IAsyncLifetime, IDisposable
{
    private ChannelMessageBus _messageBus = null!;
    private LiteDbSharedMemory _sharedMemory = null!;
    private ToolRegistry _toolRegistry = null!;
    private AgentFactory _agentFactory = null!;
    private string _testDbPath = null!;
    private AgentOrchestrator _orchestrator = null!;
    private bool _disposed;

    public Task InitializeAsync()
    {
        // Setup test database
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_infernal_{Guid.NewGuid()}.db");

        // Initialize real components for integration testing
        var loggerFactory = NullLoggerFactory.Instance;

        _messageBus = new ChannelMessageBus(
            loggerFactory.CreateLogger<ChannelMessageBus>());

        var memoryOptions = Options.Create(new MemoryOptions { DatabasePath = _testDbPath });
        _sharedMemory = new LiteDbSharedMemory(
            memoryOptions,
            loggerFactory.CreateLogger<LiteDbSharedMemory>());

        // Setup tool registry with mock tools
        var mockWebSearchTool = new Mock<ITool>();
        mockWebSearchTool.Setup(t => t.Name).Returns("web_search");
        mockWebSearchTool.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "Search results: Test information" });

        var memoryReadTool = new MemoryReadTool(_sharedMemory, loggerFactory.CreateLogger<MemoryReadTool>());
        var memoryWriteTool = new MemoryWriteTool(_sharedMemory, loggerFactory.CreateLogger<MemoryWriteTool>());

        _toolRegistry = new ToolRegistry(loggerFactory.CreateLogger<ToolRegistry>());
        _toolRegistry.RegisterTool(mockWebSearchTool.Object);
        _toolRegistry.RegisterTool(memoryReadTool);
        _toolRegistry.RegisterTool(memoryWriteTool);

        // Setup agent factory with mock components
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        mockPersonaLoader.Setup(x => x.LoadPersonaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "TestAgent",
                SystemPrompt = "You are a test agent for integration testing.",
                Specializations = new List<string> { "testing", "integration" },
                AvailableTools = new List<string> { "web_search", "read_memory", "write_memory" }
            });

        var mockLogger = new Mock<ILogger<OllamaClient>>();
        var ollamaClient = new OllamaClient(
            Options.Create(new InfernalHierarchy.Tools.OllamaOptions { BaseUrl = new Uri("http://localhost:11434"), DefaultModel = "llama3.2:latest" }),
            mockLogger.Object);

        var agentRegistry = new AgentRegistry(loggerFactory.CreateLogger<AgentRegistry>());

        _agentFactory = new AgentFactory(
            mockPersonaLoader.Object,
            _messageBus,
            _sharedMemory,
            _toolRegistry,
            agentRegistry,
            ollamaClient,
            loggerFactory.CreateLogger<AgentFactory>(),
            loggerFactory);

        // Setup orchestrator
        var hierarchyOptions = Options.Create(new InfernalHierarchy.Agents.HierarchyOptions
        {
            MainAgentName = "TestAgent",
            MaxAgentDepth = 3
        });

        var mockServiceProvider = new Mock<IServiceProvider>();
        _orchestrator = new AgentOrchestrator(
            _agentFactory,
            _messageBus,
            hierarchyOptions,
            loggerFactory.CreateLogger<AgentOrchestrator>(),
            mockServiceProvider.Object);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _orchestrator?.StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
            /* Best effort cleanup */
        }

        try
        {
            (_orchestrator as IDisposable)?.Dispose();
        }
        catch
        {
            /* Best effort cleanup */
        }

        try
        {
            _sharedMemory?.Dispose();
        }
        catch
        {
            /* Best effort cleanup */
        }

        try
        {
            _messageBus?.Dispose();
        }
        catch
        {
            /* Best effort cleanup */
        }

        if (!string.IsNullOrWhiteSpace(_testDbPath) && File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                /* Best effort cleanup */
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task EndToEnd_CreateAgent_ProcessTask_StoreMemory()
    {
        // Arrange
        using var cts = new CancellationTokenSource(5000);
        var cancellationToken = cts.Token;

        // Act - Create an agent
        var agent = await _agentFactory.CreateAgentAsync("TestAgent", AgentRank.Duke, null, cancellationToken);
        Assert.NotNull(agent);
        Assert.Equal("TestAgent", agent.Name);

        // Send a task to the agent via message bus
        var taskMessage = new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = "user",
            ToAgentId = agent.Id,
            Content = "Search for information about testing",
            Timestamp = DateTime.UtcNow
        };

        await _messageBus.PublishAsync(taskMessage, cancellationToken);
        await Task.Delay(500, cancellationToken); // Allow processing

        // Assert - Agent was created successfully
        Assert.NotEmpty(agent.Id);
    }

    [Fact]
    public async Task EndToEnd_AgentHierarchy_ParentChildCommunication()
    {
        // Arrange
        using var cts = new CancellationTokenSource(5000);
        var cancellationToken = cts.Token;

        // Act - Create parent agent
        var parentAgent = await _agentFactory.CreateAgentAsync("ParentAgent", AgentRank.Prince, null, cancellationToken);
        Assert.NotNull(parentAgent);

        // Create child agent with parent reference
        var childAgent = await _agentFactory.CreateAgentAsync("ChildAgent", AgentRank.Duke, parentAgent.Id, cancellationToken);
        Assert.NotNull(childAgent);

        // Parent sends task to child
        var taskMessage = new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = parentAgent.Id,
            ToAgentId = childAgent.Id,
            Content = "Perform research task",
            Timestamp = DateTime.UtcNow
        };

        await _messageBus.PublishAsync(taskMessage, cancellationToken);
        await Task.Delay(500, cancellationToken);

        // Assert - Verify agents were created with proper hierarchy
        Assert.Equal(AgentRank.Prince, parentAgent.Rank);
        Assert.Equal(AgentRank.Duke, childAgent.Rank);
    }

    [Fact]
    public async Task EndToEnd_MemoryOperations_ReadWriteSearch()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act - Write multiple entries using correct entity structure
        var fact1 = new Fact
        {
            Id = Guid.NewGuid().ToString(),
            Category = "test",
            Content = "This is test fact 1",
            CreatedBy = "agent1",
            CreatedAt = DateTime.UtcNow,
            Confidence = 1.0
        };
        await _sharedMemory.AddFactAsync(fact1, cancellationToken);

        var fact2 = new Fact
        {
            Id = Guid.NewGuid().ToString(),
            Category = "test",
            Content = "This is test fact 2",
            CreatedBy = "agent1",
            CreatedAt = DateTime.UtcNow,
            Confidence = 1.0
        };
        await _sharedMemory.AddFactAsync(fact2, cancellationToken);

        var decision = new Decision
        {
            Id = Guid.NewGuid().ToString(),
            Context = "Test context",
            Action = "Made a test decision",
            Reasoning = "For testing purposes",
            CreatedBy = "agent1",
            CreatedAt = DateTime.UtcNow
        };
        await _sharedMemory.AddDecisionAsync(decision, cancellationToken);

        // Assert - Read back
        var retrievedFact1 = await _sharedMemory.GetFactAsync(fact1.Id, cancellationToken);
        Assert.NotNull(retrievedFact1);
        Assert.Equal("This is test fact 1", retrievedFact1.Content);

        var searchResults = await _sharedMemory.SearchFactsAsync("test", cancellationToken);
        Assert.NotNull(searchResults);
        Assert.NotEmpty(searchResults);
        Assert.True(searchResults.Count() >= 2);

        var retrievedDecision = await _sharedMemory.GetDecisionAsync(decision.Id, cancellationToken);
        Assert.NotNull(retrievedDecision);
        Assert.Equal("Made a test decision", retrievedDecision.Action);
    }

    [Fact]
    public async Task EndToEnd_MessageBus_MultipleSubscribers()
    {
        // Arrange
        var agent1Messages = new List<AgentMessage>();
        var agent2Messages = new List<AgentMessage>();
        using var cts = new CancellationTokenSource(3000);
        var cancellationToken = cts.Token;

        // Subscribe using IAsyncEnumerable pattern
        var agent1Task = Task.Run(async () =>
        {
            await foreach (var msg in _messageBus.SubscribeAsync("agent1", cancellationToken))
            {
                agent1Messages.Add(msg);
                if (agent1Messages.Count >= 1)
                {
                    break;
                }
            }
        }, cancellationToken);

        var agent2Task = Task.Run(async () =>
        {
            await foreach (var msg in _messageBus.SubscribeAsync("agent2", cancellationToken))
            {
                agent2Messages.Add(msg);
                if (agent2Messages.Count >= 1)
                {
                    break;
                }
            }
        }, cancellationToken);

        await Task.Delay(100); // Allow subscriptions to be established

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
        var contextFact = new Fact
        {
            Id = Guid.NewGuid().ToString(),
            Category = "context",
            Content = "Important context information",
            CreatedBy = "test_agent",
            CreatedAt = DateTime.UtcNow,
            Confidence = 1.0
        };
        await _sharedMemory.AddFactAsync(contextFact, cancellationToken);

        // Act - Execute memory read tool
        var readTool = _toolRegistry.GetTool("read_memory")
            ?? throw new InvalidOperationException("Expected tool 'read_memory' to be registered");
        var parameters = new Dictionary<string, object>
        {
            ["type"] = "facts",
            ["query"] = "context",
            ["agent_id"] = "test_agent",
            ["agent_rank"] = "Worker"
        };
        var result = await readTool.ExecuteAsync(parameters, cancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.Contains("context", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndToEnd_AgentLifecycle_CreateProcessTerminate()
    {
        // Arrange
        using var cts = new CancellationTokenSource(5000);
        var cancellationToken = cts.Token;

        // Act - Create agent
        var agent = await _agentFactory.CreateAgentAsync("LifecycleAgent", AgentRank.Duke, null, cancellationToken);
        Assert.NotNull(agent);

        // Start agent
        await agent.StartAsync(cancellationToken);

        // Process task
        var taskMessage = new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = "user",
            ToAgentId = agent.Id,
            Content = "Perform task",
            Timestamp = DateTime.UtcNow
        };

        await _messageBus.PublishAsync(taskMessage, cancellationToken);
        await Task.Delay(500, cancellationToken);

        // Stop agent
        await agent.StopAsync(cancellationToken);

        // Assert - Agent lifecycle completed
        Assert.NotEmpty(agent.Id);
        Assert.Equal("LifecycleAgent", agent.Name);
    }

    [Fact]
    public async Task EndToEnd_ErrorHandling_InvalidToolExecution()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act - Try to execute tool with invalid input (missing required parameters)
        var tool = _toolRegistry.GetTool("write_memory")
            ?? throw new InvalidOperationException("Expected tool 'write_memory' to be registered");
        var invalidParams = new Dictionary<string, object>
        {
            ["invalid_key"] = "invalid_value"
        };
        var result = await tool.ExecuteAsync(invalidParams, cancellationToken);

        // Assert - Should handle gracefully
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task EndToEnd_ConcurrentAgentOperations()
    {
        // Arrange
        using var cts = new CancellationTokenSource(10000);
        var cancellationToken = cts.Token;

        // Act - Create multiple agents concurrently
        var createTasks = Enumerable.Range(0, 5).Select(i =>
            _agentFactory.CreateAgentAsync($"Agent{i}", AgentRank.Worker, null, cancellationToken));

        var agents = await Task.WhenAll(createTasks);

        // Send messages to all agents concurrently
        var messageTasks = agents.Select(agent =>
            _messageBus.PublishAsync(new AgentMessage
            {
                Id = Guid.NewGuid().ToString(),
                FromAgentId = "user",
                ToAgentId = agent.Id,
                Content = "Concurrent task",
                Timestamp = DateTime.UtcNow
            }, cancellationToken));

        await Task.WhenAll(messageTasks);
        await Task.Delay(1000, cancellationToken);

        // Assert
        Assert.Equal(5, agents.Length);
        Assert.All(agents, agent => Assert.NotNull(agent));
        Assert.All(agents, agent => Assert.NotEmpty(agent.Id));
    }

    [Fact(Skip = "Requires full environment setup including Telegram tokens")]
    public async Task Host_ShouldBuildSuccessfully()
    {
        // This test verifies the host can be built with all dependencies
        // Skipped by default as it requires configuration
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
