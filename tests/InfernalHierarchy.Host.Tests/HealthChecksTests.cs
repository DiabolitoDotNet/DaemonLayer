using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Telegram.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using Telegram.Bot;
using Telegram.Bot.Types;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class OllamaHealthCheckTests
{
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly OllamaOptions _options;

    public OllamaHealthCheckTests()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();

        _options = new OllamaOptions
        {
            BaseUrl = new Uri("http://localhost:11434/v1"),
            DefaultModel = "llama3.1:8b"
        };
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOllamaIsAccessible_ShouldReturnHealthy()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"models\":[]}")
            });

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var healthCheck = new OllamaHealthCheck(
            Options.Create(_options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("Ollama is accessible");
        result.Data.Should().ContainKey("url");
        result.Data.Should().ContainKey("model");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOllamaReturnsError_ShouldReturnDegraded()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var healthCheck = new OllamaHealthCheck(
            Options.Create(_options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("InternalServerError");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOllamaIsUnreachable_ShouldReturnUnhealthy()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var healthCheck = new OllamaHealthCheck(
            Options.Create(_options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Cannot connect to Ollama");
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTimedOut_ShouldReturnUnhealthy()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var healthCheck = new OllamaHealthCheck(
            Options.Create(_options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("timed out");
    }
}

public class TelegramHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WithEmptyToken_ShouldReturnDegraded()
    {
        // Arrange
        var options = new TelegramOptions { BotToken = "" };
        var factory = new Mock<ITelegramBotClientFactory>(MockBehavior.Strict);
        var healthCheck = new TelegramHealthCheck(Options.Create(options), factory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not configured");

        factory.Verify(f => f.Create(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckHealthAsync_WithInvalidToken_ShouldReturnUnhealthy()
    {
        // Arrange
        var options = new TelegramOptions { BotToken = "invalid_token" };
        var botClient = new Mock<ITelegramBotClientProbe>(MockBehavior.Strict);
        botClient
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var factory = new Mock<ITelegramBotClientFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.Create(options.BotToken))
            .Returns(botClient.Object);

        var healthCheck = new TelegramHealthCheck(Options.Create(options), factory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Cannot connect to Telegram");
        result.Exception.Should().NotBeNull();

        factory.VerifyAll();
        botClient.VerifyAll();
    }

    // Note: Testing valid Telegram connection requires real token or extensive mocking
    // of TelegramBotClient which is sealed. Integration tests are better suited for this.
}

public class QdrantHealthCheckTests
{
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;

    public QdrantHealthCheckTests()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenVectorMemoryDisabled_ShouldReturnHealthy()
    {
        // Arrange
        var options = new VectorMemoryOptions
        {
            Enabled = false,
            QdrantUrl = new Uri("http://localhost:6333"),
            CollectionName = "infernal_facts",
            VectorDimensions = 384,
        };

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var healthCheck = new QdrantHealthCheck(
            Options.Create(options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("disabled");
        result.Data.Should().ContainKey("enabled");
        result.Data["enabled"].Should().Be(false);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenQdrantIsAccessible_ShouldReturnHealthy()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":{\"collections\":[]},\"status\":\"ok\"}")
            });

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var options = new VectorMemoryOptions
        {
            Enabled = true,
            QdrantUrl = new Uri("http://localhost:6333"),
            CollectionName = "infernal_facts",
            VectorDimensions = 384,
        };

        var healthCheck = new QdrantHealthCheck(
            Options.Create(options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("Qdrant is accessible");
        result.Data.Should().ContainKey("url");
        result.Data.Should().ContainKey("collection");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenQdrantReturnsError_ShouldReturnDegraded()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var options = new VectorMemoryOptions
        {
            Enabled = true,
            QdrantUrl = new Uri("http://localhost:6333"),
            CollectionName = "infernal_facts",
            VectorDimensions = 384,
        };

        var healthCheck = new QdrantHealthCheck(
            Options.Create(options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("InternalServerError");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenQdrantIsUnreachable_ShouldReturnUnhealthy()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var options = new VectorMemoryOptions
        {
            Enabled = true,
            QdrantUrl = new Uri("http://localhost:6333"),
            CollectionName = "infernal_facts",
            VectorDimensions = 384,
        };

        var healthCheck = new QdrantHealthCheck(
            Options.Create(options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Cannot connect to Qdrant");
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTimedOut_ShouldReturnUnhealthy()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        using var httpClient = new HttpClient(_mockHttpHandler.Object, disposeHandler: false);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var options = new VectorMemoryOptions
        {
            Enabled = true,
            QdrantUrl = new Uri("http://localhost:6333"),
            CollectionName = "infernal_facts",
            VectorDimensions = 384,
        };

        var healthCheck = new QdrantHealthCheck(
            Options.Create(options),
            _mockHttpClientFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("timed out");
    }
}

public class LiteDbHealthCheckTests
{
    private readonly Mock<ISharedMemory> _mockSharedMemory;
    private readonly MemoryOptions _options;

    public LiteDbHealthCheckTests()
    {
        _mockSharedMemory = new Mock<ISharedMemory>();
        _options = new MemoryOptions
        {
            DatabasePath = "./test_memory.db"
        };
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDatabaseIsAccessible_ShouldReturnHealthy()
    {
        // Arrange
        _mockSharedMemory
            .Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InfernalHierarchy.Core.Entities.Decision>());

        var healthCheck = new LiteDbHealthCheck(
            _mockSharedMemory.Object,
            Options.Create(_options));

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("LiteDB is accessible");
        result.Data.Should().ContainKey("database_path");
        result.Data.Should().ContainKey("database_exists");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDatabaseFails_ShouldReturnUnhealthy()
    {
        // Arrange
        _mockSharedMemory
            .Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var healthCheck = new LiteDbHealthCheck(
            _mockSharedMemory.Object,
            Options.Create(_options));

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("health check failed");
        result.Exception.Should().NotBeNull();
    }
}

public class AgentHierarchyHealthCheckTests
{
    private readonly Mock<IAgentFactory> _mockFactory;

    public AgentHierarchyHealthCheckTests()
    {
        _mockFactory = new Mock<IAgentFactory>();
    }

    private static IAgent CreateAgent(InfernalHierarchy.Core.Entities.AgentRank rank, InfernalHierarchy.Core.Entities.AgentStatus status = InfernalHierarchy.Core.Entities.AgentStatus.Idle)
    {
        var mock = new Mock<IAgent>();
        mock.SetupGet(a => a.Id).Returns(Guid.NewGuid().ToString());
        mock.SetupGet(a => a.Name).Returns("TestAgent");
        mock.SetupGet(a => a.Rank).Returns(rank);
        mock.SetupGet(a => a.Status).Returns(status);
        mock.SetupGet(a => a.Persona).Returns(new InfernalHierarchy.Core.Entities.Persona
        {
            Name = "Test",
            SystemPrompt = "Test",
            Specializations = new List<string>(),
            AvailableTools = new List<string>()
        });
        return mock.Object;
    }

    [Fact]
    public async Task CheckHealthAsync_WithHealthyHierarchy_ShouldReturnHealthy()
    {
        // Arrange
        _mockFactory
            .Setup(x => x.GetAllAgents())
            .Returns(new List<IAgent>
            {
                CreateAgent(InfernalHierarchy.Core.Entities.AgentRank.Supreme),
                CreateAgent(InfernalHierarchy.Core.Entities.AgentRank.Prince),
                CreateAgent(InfernalHierarchy.Core.Entities.AgentRank.Duke)
            });

        var healthCheck = new AgentHierarchyHealthCheck(_mockFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("agent(s) running");
        result.Data.Should().ContainKey("total_agents");
        result.Data["total_agents"].Should().Be(3);
    }

    [Fact]
    public async Task CheckHealthAsync_WithNoAgents_ShouldReturnDegraded()
    {
        // Arrange
        _mockFactory
            .Setup(x => x.GetAllAgents())
            .Returns(new List<IAgent>());

        var healthCheck = new AgentHierarchyHealthCheck(_mockFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("No agents are currently running");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRegistryFails_ShouldReturnUnhealthy()
    {
        // Arrange
        _mockFactory
            .Setup(x => x.GetAllAgents())
            .Throws(new InvalidOperationException("Factory access failed"));

        var healthCheck = new AgentHierarchyHealthCheck(_mockFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Agent hierarchy check failed");
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldIncludeAgentCountsByRank()
    {
        // Arrange
        _mockFactory
            .Setup(x => x.GetAllAgents())
            .Returns(new List<IAgent>
            {
                CreateAgent(InfernalHierarchy.Core.Entities.AgentRank.Supreme),
                CreateAgent(InfernalHierarchy.Core.Entities.AgentRank.Prince),
                CreateAgent(InfernalHierarchy.Core.Entities.AgentRank.Prince),
                CreateAgent(InfernalHierarchy.Core.Entities.AgentRank.Duke),
                CreateAgent(InfernalHierarchy.Core.Entities.AgentRank.Worker)
            });

        var healthCheck = new AgentHierarchyHealthCheck(_mockFactory.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Data.Should().ContainKey("supreme_count");
        result.Data.Should().ContainKey("prince_count");
        result.Data.Should().ContainKey("duke_count");
        result.Data.Should().ContainKey("worker_count");

        result.Data["supreme_count"].Should().Be(1);
        result.Data["prince_count"].Should().Be(2);
        result.Data["duke_count"].Should().Be(1);
        result.Data["worker_count"].Should().Be(1);
    }
}
