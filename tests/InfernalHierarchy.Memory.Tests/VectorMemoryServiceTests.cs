using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public class VectorMemoryServiceTests : IDisposable
{
    private readonly Mock<ISharedMemory> _mockSharedMemory;
    private readonly Mock<ILogger<VectorMemoryService>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;
    private readonly VectorMemoryOptions _options;
    private readonly OnnxEmbeddingService _embeddingService;
    private readonly VectorMemoryService _sut;

    public VectorMemoryServiceTests()
    {
        _mockSharedMemory = new Mock<ISharedMemory>();
        _mockLogger = new Mock<ILogger<VectorMemoryService>>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object);
        
        _options = new VectorMemoryOptions
        {
            Enabled = true,
            QdrantUrl = new Uri("http://localhost:6333"),
            CollectionName = "test_collection",
            VectorDimensions = 384
        };

        var mockOptions = Options.Create(_options);
        _embeddingService = new OnnxEmbeddingService(
            Options.Create(new OnnxEmbeddingOptions
            {
                Enabled = true,
                ModelPath = "./does-not-exist.onnx",
                TokenizerPath = "./does-not-exist.tokenizer.json",
                EmbeddingDimension = 384,
            }),
            Mock.Of<ILogger<OnnxEmbeddingService>>());

        _sut = new VectorMemoryService(
            _httpClient,
            mockOptions,
            _mockSharedMemory.Object,
            _embeddingService,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task InitializeCollectionAsync_WhenCollectionExists_ShouldLogAndReturn()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("test_collection")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _sut.InitializeCollectionAsync();

        // Assert
        _mockHttpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task InitializeCollectionAsync_WhenCollectionDoesNotExist_ShouldCreateIt()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _sut.InitializeCollectionAsync();

        // Assert
        _mockHttpHandler.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task StoreFactWithVectorAsync_ShouldStoreInBothLiteDbAndQdrant()
    {
        // Arrange
        var fact = new Fact
        {
            Id = "fact_123",
            Category = "Technical",
            Content = "Test fact content",
            Source = "test",
            Confidence = 0.95f,
            CreatedBy = "agent_1",
            CreatedAt = DateTime.UtcNow
        };

        var embedding = new float[384];
        for (int i = 0; i < embedding.Length; i++)
            embedding[i] = 0.1f;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}")
            });

        // Act
        await _sut.StoreFactWithVectorAsync(fact, embedding);

        // Assert
        _mockSharedMemory.Verify(x => x.AddFactAsync(fact, It.IsAny<CancellationToken>()), Times.Once);
        _mockHttpHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Put &&
                req.RequestUri!.ToString().Contains("points")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SearchSimilarAsync_WithValidEmbedding_ShouldReturnSimilarFacts()
    {
        // Arrange
        var queryEmbedding = new float[384];
        for (int i = 0; i < queryEmbedding.Length; i++)
            queryEmbedding[i] = 0.2f;

        var searchResponse = new
        {
            result = new[]
            {
                new
                {
                    id = "fact_1",
                    score = 0.95,
                    payload = new
                    {
                        Category = "Technical",
                        Content = "Similar fact 1",
                        Source = "test",
                        Confidence = 0.9,
                        CreatedBy = "agent_1",
                        CreatedAt = DateTime.UtcNow
                    }
                },
                new
                {
                    id = "fact_2",
                    score = 0.85,
                    payload = new
                    {
                        Category = "General",
                        Content = "Similar fact 2",
                        Source = "test",
                        Confidence = 0.8,
                        CreatedBy = "agent_2",
                        CreatedAt = DateTime.UtcNow
                    }
                }
            }
        };

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(searchResponse))
            });

        _mockSharedMemory
            .Setup(x => x.GetFactAsync("fact_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fact
            {
                Id = "fact_1",
                Category = "Technical",
                Content = "Similar fact 1",
                Source = "test",
                Confidence = 0.9,
                CreatedBy = "agent_1",
                CreatedAt = DateTime.UtcNow,
            });

        _mockSharedMemory
            .Setup(x => x.GetFactAsync("fact_2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fact
            {
                Id = "fact_2",
                Category = "General",
                Content = "Similar fact 2",
                Source = "test",
                Confidence = 0.8,
                CreatedBy = "agent_2",
                CreatedAt = DateTime.UtcNow,
            });

        // Act
        var results = (await _sut.SearchSimilarAsync(queryEmbedding, limit: 5)).ToList();

        // Assert
        results.Should().HaveCount(2);
        results[0].Id.Should().Be("fact_1");
        results[0].Content.Should().Be("Similar fact 1");
        results[1].Id.Should().Be("fact_2");
    }

    [Fact]
    public async Task SearchSimilarVisibleFactsAsync_WhenVectorResultsContainPrivateFacts_ShouldFilterByVisibility()
    {
        // Arrange
        var searchResponse = new
        {
            result = new[]
            {
                new { id = "fact_1", score = 0.95, payload = new { } },
                new { id = "fact_2", score = 0.90, payload = new { } }
            }
        };

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(searchResponse))
            });

        _mockSharedMemory
            .Setup(x => x.GetFactAsync("fact_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fact
            {
                Id = "fact_1",
                Category = "General",
                Content = "Visible fact",
                Source = "test",
                Confidence = 0.9,
                CreatedBy = "agent_1",
                Visibility = MemoryVisibility.Private
            });

        _mockSharedMemory
            .Setup(x => x.GetFactAsync("fact_2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fact
            {
                Id = "fact_2",
                Category = "General",
                Content = "Not visible fact",
                Source = "test",
                Confidence = 0.9,
                CreatedBy = "other_agent",
                Visibility = MemoryVisibility.Private
            });

        // Act
        var results = await _sut.SearchSimilarVisibleFactsAsync(
            query: "any query",
            requestingAgentId: "agent_1",
            requestingAgentRank: AgentRank.Worker,
            limit: 10,
            minScore: 0.7);

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fact_1");
        results[0].Content.Should().Be("Visible fact");

        _mockSharedMemory.Verify(
            x => x.SearchVisibleFactsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AgentRank>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchSimilarAsync_WithNoResults_ShouldReturnEmptyList()
    {
        // Arrange
        var queryEmbedding = new float[384];
        var searchResponse = new { result = Array.Empty<object>() };

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(searchResponse))
            });

        // Act
        var results = await _sut.SearchSimilarAsync(queryEmbedding);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ShouldReturnDeterministicEmbedding()
    {
        // Arrange
        var queryText = "test query";

        // Act
        var embedding1 = await _sut.GenerateEmbeddingAsync(queryText);
        var embedding2 = await _sut.GenerateEmbeddingAsync(queryText);

        // Assert
        embedding1.Should().NotBeNull();
        embedding1.Should().HaveCount(384);
        embedding2.Should().HaveCount(384);
        embedding1.Should().Equal(embedding2);
    }

    [Fact]
    public async Task StoreFactWithVectorAsync_WhenQdrantFails_ShouldThrowException()
    {
        // Arrange
        var fact = new Fact
        {
            Id = "fact_error",
            Content = "Test",
            Category = "Test",
            Source = "test",
            Confidence = 0.8f,
            CreatedBy = "agent",
            CreatedAt = DateTime.UtcNow
        };
        var embedding = new float[384];

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _sut.StoreFactWithVectorAsync(fact, embedding));
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _embeddingService?.Dispose();
        GC.SuppressFinalize(this);
    }
}
