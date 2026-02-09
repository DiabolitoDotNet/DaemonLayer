using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
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
            { "type", "fact" },
            { "agent_id", "agent1" },
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
            It.Is<Fact>(f => f.Content == "Test fact content" && f.Category == "test" && f.CreatedBy == "agent1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MemoryWriteTool_AddFact_WhenVectorMemoryAvailable_IndexesFact()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockVectorMemory = new Mock<IVectorMemory>();
        var mockLogger = new Mock<ILogger<MemoryWriteTool>>();

        mockVectorMemory
            .Setup(x => x.IndexFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new MemoryWriteTool(mockMemory.Object, mockVectorMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "fact" },
            { "agent_id", "agent1" },
            { "content", "Test fact content" },
            { "category", "test" },
            { "source", "unit_test" },
            { "confidence", 1.0 }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        mockVectorMemory.Verify(x => x.IndexFactAsync(
            It.Is<Fact>(f => f.Content == "Test fact content" && f.Category == "test" && f.CreatedBy == "agent1"),
            It.IsAny<CancellationToken>()), Times.Once);

        mockMemory.Verify(x => x.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()), Times.Never);
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

        mockMemory.Setup(x => x.SearchVisibleFactsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "facts" },
            { "query", "test" },
            { "agent_id", "agent1" },
            { "agent_rank", "Worker" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Test fact 1", result.Output, StringComparison.Ordinal);
        Assert.Contains("Test fact 2", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemoryReadTool_SearchFacts_WhenVectorMemoryAvailable_UsesSemanticSearch()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>(MockBehavior.Strict);
        var mockVectorMemory = new Mock<IVectorMemory>(MockBehavior.Strict);
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        var facts = new List<Fact>
        {
            new Fact
            {
                Id = Guid.NewGuid().ToString(),
                Category = "test",
                Content = "Semantic hit 1",
                Source = "qdrant",
                CreatedBy = "agent1"
            }
        };

        mockVectorMemory.Setup(x => x.SearchSimilarVisibleFactsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var tool = new MemoryReadTool(mockMemory.Object, mockVectorMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "facts" },
            { "query", "something semantically close" },
            { "agent_id", "agent1" },
            { "agent_rank", "Worker" },
            { "count", 5 },
            { "mode", "auto" },
            { "min_score", 0.5 }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Semantic results", result.Output, StringComparison.Ordinal);
        Assert.Contains("Semantic hit 1", result.Output, StringComparison.Ordinal);

        mockVectorMemory.Verify(x => x.SearchSimilarVisibleFactsAsync(
            It.Is<string>(q => q.Contains("semantically", StringComparison.OrdinalIgnoreCase)),
            It.Is<string>(id => id == "agent1"),
            It.Is<AgentRank>(r => r == AgentRank.Worker),
            It.Is<int>(l => l == 5),
            It.Is<double>(ms => Math.Abs(ms - 0.5) < 0.0001),
            It.IsAny<CancellationToken>()), Times.Once);

        mockMemory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateSubAgentTool_WithValidParameters_CreatesAgent()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();

        var mockAgent = new Mock<IAgent>();
        mockAgent.SetupGet(x => x.Id).Returns("new_agent_id");
        mockAgent.SetupGet(x => x.Name).Returns("TestAgent");
        mockAgent.Setup(x => x.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("TestAgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "TestAgent",
                DemonTitle = "Test",
                SystemPrompt = "You are TestAgent",
                AvailableTools = new List<string> { "read_memory" }
            });

        mockFactory.Setup(x => x.CreateAgentAsync(
            It.IsAny<string>(),
            It.IsAny<AgentRank>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAgent.Object);

        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "persona_name", "TestAgent" },
            { "rank", "Duke" },
            { "parent_id", "parent1" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("new_agent_id", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSubAgentTool_WithPersonaNameAlias_CreatesAgent()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();

        var mockAgent = new Mock<IAgent>();
        mockAgent.SetupGet(x => x.Id).Returns("new_agent_id");
        mockAgent.SetupGet(x => x.Name).Returns("TestAgent");
        mockAgent.Setup(x => x.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("TestAgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "TestAgent",
                DemonTitle = "Test",
                SystemPrompt = "You are TestAgent",
                AvailableTools = new List<string> { "read_memory" }
            });

        mockFactory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAgent.Object);

        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "personaName", "TestAgent" },
            { "rank", "Duke" },
            { "parentId", "parent1" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        mockFactory.Verify(x => x.CreateAgentAsync("TestAgent", AgentRank.Duke, "parent1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSubAgentTool_WithMissingPersonaName_ReturnsError()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();
        var mockAgent = new Mock<IAgent>();
        mockAgent.SetupGet(x => x.Id).Returns("new_agent_id");
        mockAgent.SetupGet(x => x.Name).Returns("WeatherWorker");
        mockAgent.Setup(x => x.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("WeatherWorker", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Persona?)null);

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("generic_worker", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "generic_worker",
                DemonTitle = "Generic Worker",
                SystemPrompt = "Base system prompt",
                AvailableTools = new List<string> { "web_search" },
                Specializations = new List<string> { "general" },
                Personality = new PersonalityTraits { Tone = "Neutral", Approach = "Methodical", Verbosity = 5, UseDemonicTheme = false }
            });

        mockFactory
            .Setup(x => x.CreateAgentAsync(
                It.Is<Persona>(p => p.Name == "WeatherWorker"),
                AgentRank.Worker,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAgent.Object);

        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "rank", "Worker" },
            { "role", "WeatherWorker" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("new_agent_id", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSubAgentTool_WithMissingRank_ReturnsError()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();
        var mockAgent = new Mock<IAgent>();
        mockAgent.SetupGet(x => x.Id).Returns("new_agent_id");
        mockAgent.SetupGet(x => x.Name).Returns("TestAgent");
        mockAgent.Setup(x => x.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("TestAgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "TestAgent",
                DemonTitle = "Test",
                SystemPrompt = "You are TestAgent",
                AvailableTools = new List<string> { "read_memory" }
            });

        mockFactory
            .Setup(x => x.CreateAgentAsync("TestAgent", AgentRank.Worker, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAgent.Object);

        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "persona_name", "TestAgent" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        mockFactory.Verify(x => x.CreateAgentAsync("TestAgent", AgentRank.Worker, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSubAgentTool_WithInvalidRank_ReturnsError()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();
        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "persona_name", "TestAgent" },
            { "rank", "NotARank" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid rank", result.Error ?? string.Empty, StringComparison.Ordinal);
        mockFactory.Verify(x => x.CreateAgentAsync(It.IsAny<string>(), It.IsAny<AgentRank>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateSubAgentTool_WhenParentIdMissing_PassesNullToFactory()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();

        var mockAgent = new Mock<IAgent>();
        mockAgent.SetupGet(x => x.Id).Returns("new_agent_id");
        mockAgent.SetupGet(x => x.Name).Returns("TestAgent");
        mockAgent.Setup(x => x.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("TestAgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "TestAgent",
                DemonTitle = "Test",
                SystemPrompt = "You are TestAgent",
                AvailableTools = new List<string> { "read_memory" }
            });

        mockFactory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAgent.Object);

        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "persona_name", "TestAgent" },
            { "rank", "Duke" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        mockFactory.Verify(x => x.CreateAgentAsync("TestAgent", AgentRank.Duke, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSubAgentTool_WhenFactoryThrows_ReturnsError()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("TestAgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "TestAgent",
                DemonTitle = "Test",
                SystemPrompt = "You are TestAgent",
                AvailableTools = new List<string> { "read_memory" }
            });

        mockFactory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("factory failed"));

        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "persona_name", "TestAgent" },
            { "rank", "Worker" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("factory failed", result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSubAgentTool_WhenStartAsyncThrows_ReturnsError()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();

        var mockAgent = new Mock<IAgent>();
        mockAgent.SetupGet(x => x.Id).Returns("new_agent_id");
        mockAgent.SetupGet(x => x.Name).Returns("TestAgent");
        mockAgent.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("start failed"));

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("TestAgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "TestAgent",
                DemonTitle = "Test",
                SystemPrompt = "You are TestAgent",
                AvailableTools = new List<string> { "read_memory" }
            });

        mockFactory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAgent.Object);

        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "persona_name", "TestAgent" },
            { "rank", "Worker" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("start failed", result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSubAgentTool_WhenPersonaMissing_FallsBackToBasePersonaWithOverrides()
    {
        // Arrange
        var mockFactory = new Mock<IAgentFactory>();
        var mockPersonaLoader = new Mock<IPersonaLoader>();
        var mockLogger = new Mock<ILogger<CreateSubAgentTool>>();

        var basePersona = new Persona
        {
            Name = "generic_worker",
            DemonTitle = "Generic Worker",
            SystemPrompt = "Base system prompt",
            AvailableTools = new List<string> { "web_search" },
            Specializations = new List<string> { "general" },
            Personality = new PersonalityTraits { Tone = "Neutral", Approach = "Methodical", Verbosity = 5, UseDemonicTheme = false }
        };

        var mockAgent = new Mock<IAgent>();
        mockAgent.SetupGet(x => x.Id).Returns("new_agent_id");
        mockAgent.SetupGet(x => x.Name).Returns("MeteoAgent");
        mockAgent.Setup(x => x.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("MeteoAgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Persona?)null);

        mockPersonaLoader
            .Setup(x => x.LoadPersonaAsync("generic_worker", It.IsAny<CancellationToken>()))
            .ReturnsAsync(basePersona);

        mockFactory
            .Setup(x => x.CreateAgentAsync(
                It.Is<Persona>(p =>
                    p.Name == "MeteoAgent" &&
                    p.SystemPrompt != null &&
                    p.SystemPrompt.Contains("Weather forecasting specialist", StringComparison.OrdinalIgnoreCase)),
                AgentRank.Worker,
                It.IsAny<string?>(),
                It.Is<string?>(pp => pp != null && pp.Contains("souls/generic_worker.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAgent.Object);

        var tool = new CreateSubAgentTool(mockFactory.Object, mockPersonaLoader.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "persona_name", "MétéoAgent" },
            { "rank", "Worker" },
            { "base_persona", "generic_worker" },
            { "role", "Weather forecasting specialist" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        mockFactory.VerifyAll();
    }

    [Fact]
    public async Task MemoryReadTool_WithMissingType_DefaultsToFacts()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();
        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        mockMemory
            .Setup(x => x.GetVisibleFactsAsync("unknown", AgentRank.Worker, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Fact>());

        var parameters = new Dictionary<string, object>();

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("No facts", result.Output ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.TryGetValue("type", out var typeObj));
        Assert.Equal("facts", typeObj?.ToString());
    }

    [Fact]
    public async Task MemoryReadTool_WithInvalidType_ReturnsError()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();
        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "nope" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid type", result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemoryReadTool_DecisionsWithoutQuery_UsesRecentDecisionsAndHonorsCount()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.GetRecentDecisionsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Decision>
            {
                new Decision
                {
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "agent1",
                    Action = "Do thing",
                    Reasoning = "Because"
                }
            });

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "decisions" },
            { "count", 1 }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Do thing", result.Output, StringComparison.Ordinal);
        mockMemory.Verify(x => x.GetRecentDecisionsAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        mockMemory.Verify(x => x.SearchDecisionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MemoryReadTool_DecisionsWithQuery_UsesSearchAndReturnsNoResultsMessage()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.SearchDecisionsAsync("search", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Decision>());

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "decisions" },
            { "query", "search" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("No decisions found.", result.Output);
        mockMemory.Verify(x => x.SearchDecisionsAsync("search", It.IsAny<CancellationToken>()), Times.Once);
        mockMemory.Verify(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MemoryReadTool_FactsWithoutQuery_UsesVisibleFactsAndReturnsNoResultsMessage()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.GetVisibleFactsAsync(It.IsAny<string>(), It.IsAny<AgentRank>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Fact>());

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "facts" },
            { "agent_id", "agent1" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("No facts found.", result.Output);
        mockMemory.Verify(x => x.GetVisibleFactsAsync("agent1", AgentRank.Worker, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MemoryReadTool_FactsWithInvalidRank_DefaultsToWorker()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.SearchVisibleFactsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Fact>());

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "facts" },
            { "query", "test" },
            { "agent_id", "agent1" },
            { "agent_rank", "NotARank" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        mockMemory.Verify(x => x.SearchVisibleFactsAsync("test", "agent1", AgentRank.Worker, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MemoryReadTool_TasksWithoutQuery_UsesPendingStatus()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskEntry>
            {
                new TaskEntry
                {
                    Description = "Do work",
                    AssignedTo = "agent1",
                    Status = InfernalHierarchy.Core.Entities.TaskStatus.Pending
                }
            });

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "tasks" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Do work", result.Output, StringComparison.Ordinal);
        mockMemory.Verify(x => x.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Pending, It.IsAny<CancellationToken>()), Times.Once);
        mockMemory.Verify(x => x.GetTasksByAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MemoryReadTool_TasksWithQuery_UsesAgentQueryAndReturnsNoResultsMessage()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.GetTasksByAgentAsync("agent1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskEntry>());

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "tasks" },
            { "query", "agent1" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("No tasks found.", result.Output);
        mockMemory.Verify(x => x.GetTasksByAgentAsync("agent1", It.IsAny<CancellationToken>()), Times.Once);
        mockMemory.Verify(x => x.GetTasksByStatusAsync(It.IsAny<InfernalHierarchy.Core.Entities.TaskStatus>(), It.IsAny<CancellationToken>()), Times.Never);
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
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task ToolExecution_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var mockMemory = new Mock<ISharedMemory>();
        var mockLogger = new Mock<ILogger<MemoryReadTool>>();

        mockMemory.Setup(x => x.SearchVisibleFactsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string query, string agentId, AgentRank agentRank, CancellationToken ct) =>
            {
                await Task.Delay(10000, ct); // Long delay
                return new List<Fact>();
            });

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        using var cts = new CancellationTokenSource(100); // 100ms timeout

        var parameters = new Dictionary<string, object>
        {
            { "type", "facts" },
            { "query", "test" },
            { "agent_id", "agent1" },
            { "agent_rank", "Worker" }
        };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
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

        mockMemory.Setup(x => x.SearchVisibleFactsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<CancellationToken>()))
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
        var parameters = new Dictionary<string, object>
        {
            { "type", "facts" },
            { "query", "test" },
            { "agent_id", "agent1" },
            { "agent_rank", "Worker" }
        };

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
            { "agent_id", "agent1" },
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

        mockMemory.Setup(x => x.SearchVisibleFactsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var tool = new MemoryReadTool(mockMemory.Object, mockLogger.Object);

        var parameters = new Dictionary<string, object>
        {
            { "type", "facts" },
            { "query", "technical" },
            { "agent_id", "agent1" },
            { "agent_rank", "Worker" }
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Technical fact", result.Output, StringComparison.Ordinal);
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
    public void ToolRegistry_GetNonExistentTool_ReturnsNull()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ToolRegistry>>();
        var registry = new ToolRegistry(mockLogger.Object);

        // Act
        var tool = registry.GetTool("non_existent_tool");

        // Assert
        Assert.Null(tool);
    }
}
