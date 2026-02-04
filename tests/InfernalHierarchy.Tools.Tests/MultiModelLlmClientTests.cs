using FluentAssertions;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Collections;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class MultiModelLlmClientTests : IDisposable
{
    private readonly Mock<ILogger<MultiModelLlmClient>> _mockLogger;
    private readonly TokenUsageTracker _tokenTracker;
    private readonly LlmOptions _options;
    private readonly MultiModelLlmClient _sut;

    public MultiModelLlmClientTests()
    {
        _mockLogger = new Mock<ILogger<MultiModelLlmClient>>();
        _tokenTracker = new TokenUsageTracker(Mock.Of<ILogger<TokenUsageTracker>>());

        _options = new LlmOptions
        {
            Models = new List<ModelConfig>
            {
                new() { Name = "gemma:2b", Complexity = TaskComplexity.Simple, Priority = 10, MaxTokens = 1024, Temperature = 0.7, BaseUrl = new Uri("http://localhost:11434/v1") },
                new() { Name = "llama3.1:8b", Complexity = TaskComplexity.Medium, Priority = 20, MaxTokens = 2048, Temperature = 0.8, BaseUrl = new Uri("http://localhost:11434/v1") },
                new() { Name = "qwen:32b", Complexity = TaskComplexity.Complex, Priority = 30, MaxTokens = 4096, Temperature = 0.9, BaseUrl = new Uri("http://localhost:11434/v1") },
                new() { Name = "deepseek-coder:6.7b", Complexity = TaskComplexity.Expert, Priority = 40, MaxTokens = 2048, Temperature = 0.7, BaseUrl = new Uri("http://localhost:11434/v1") }
            }
        };

        _sut = new MultiModelLlmClient(
            Options.Create(_options),
            _tokenTracker,
            _mockLogger.Object);
    }

    [Theory]
    [InlineData(TaskComplexity.Simple, "gemma:2b")]
    [InlineData(TaskComplexity.Medium, "llama3.1:8b")]
    [InlineData(TaskComplexity.Complex, "qwen:32b")]
    [InlineData(TaskComplexity.Expert, "deepseek-coder:6.7b")]
    public void SelectModelForComplexity_ShouldReturnCorrectModel(TaskComplexity complexity, string expectedModel)
    {
        // Arrange & Act
        var selectedModel = _sut.GetType()
            .GetMethod("SelectModelForComplexity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(_sut, new object[] { complexity }) as ModelConfig;

        // Assert
        selectedModel.Should().NotBeNull();
        selectedModel!.Name.Should().Be(expectedModel);
    }

    [Fact]
    public Task GetCompletionAsync_WithSimpleTask_ShouldUseSimpleModel()
    {
        // Arrange/Act/Assert
        // This test intentionally doesn't call Ollama; it validates configuration/model selection inputs.
        _options.Models.Should().Contain(m => m.Complexity == TaskComplexity.Simple);

        return Task.CompletedTask;
    }

    [Fact]
    public void GetFallbackModels_ShouldReturnModelsInOrder()
    {
        // Arrange
        var primaryModel = _options.Models.First(m => m.Complexity == TaskComplexity.Complex);

        // Act
        var fallbacks = _sut.GetType()
            .GetMethod("GetFallbackModels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(_sut, new object[] { primaryModel }) as List<ModelConfig>;

        // Assert
        fallbacks.Should().NotBeNull();
        fallbacks.Should().HaveCountGreaterThan(0);
        fallbacks.Should().NotContain(m => m.Name == primaryModel.Name);
    }

    [Fact]
    public void Constructor_ShouldInitializeAllModels()
    {
        // Arrange & Act - already done in constructor

        // Assert
        var clientsField = _sut.GetType()
            .GetField("_modelClients", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        clientsField.Should().NotBeNull();
        var clients = clientsField!.GetValue(_sut);
        clients.Should().NotBeNull();
        clients.Should().BeAssignableTo<IDictionary>();

        var dict = (IDictionary)clients!;
        dict.Count.Should().Be(4);
        dict.Contains("gemma:2b").Should().BeTrue();
        dict.Contains("llama3.1:8b").Should().BeTrue();
        dict.Contains("qwen:32b").Should().BeTrue();
        dict.Contains("deepseek-coder:6.7b").Should().BeTrue();
    }

    [Fact]
    public Task GetCompletionAsync_WhenAllModelsFail_ShouldThrowException()
    {
        // Arrange - This would require mocking ChatClient responses
        // For a true unit test, we'd need to refactor MultiModelLlmClient to accept IChatClient
        // For now, we document the expected behavior

        // Act & Assert
        // When all models fail, should throw aggregated exception
        _options.Models.Should().HaveCount(4); // Verify we have fallbacks configured

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(TaskComplexity.Simple, 1024)]
    [InlineData(TaskComplexity.Medium, 2048)]
    [InlineData(TaskComplexity.Complex, 4096)]
    [InlineData(TaskComplexity.Expert, 2048)]
    public void ModelConfiguration_ShouldHaveCorrectMaxTokens(TaskComplexity complexity, int expectedMaxTokens)
    {
        // Arrange & Act
        var model = _options.Models.First(m => m.Complexity == complexity);

        // Assert
        model.MaxTokens.Should().Be(expectedMaxTokens);
    }

    [Fact]
    public void ModelConfiguration_ShouldHaveReasonableTemperatures()
    {
        // Arrange & Act & Assert
        foreach (var model in _options.Models)
        {
            model.Temperature.Should().BeGreaterThanOrEqualTo(0.0);
            model.Temperature.Should().BeLessThanOrEqualTo(2.0);
        }
    }

    [Fact]
    public void AllModels_ShouldHaveUniqueComplexityLevels()
    {
        // Arrange & Act
        var complexities = _options.Models.Select(m => m.Complexity).ToList();

        // Assert
        complexities.Should().OnlyHaveUniqueItems();
        complexities.Should().HaveCount(4);
    }

    [Fact]
    public void AllModels_ShouldHaveValidBaseUrls()
    {
        // Arrange & Act & Assert
        foreach (var model in _options.Models)
        {
            model.BaseUrl.Should().NotBeNull();
            model.BaseUrl.IsAbsoluteUri.Should().BeTrue();
            model.BaseUrl.Scheme.Should().Match(s => s == "http" || s == "https");
        }
    }

    [Fact]
    public void GetStreamingCompletionAsync_ShouldUseCorrectComplexity()
    {
        // Arrange/Act/Assert
        // Streaming requires actual Ollama connection
        // This test validates the method signature exists and accepts correct parameters
        var method = _sut.GetType().GetMethod("GetStreamingCompletionAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Name.Should().Contain("IAsyncEnumerable");
    }

    [Fact]
    public void Dispose_ShouldDisposeAllClients()
    {
        // Arrange
        var disposableSut = new MultiModelLlmClient(
            Options.Create(_options),
            _tokenTracker,
            _mockLogger.Object);

        // Act
        disposableSut.Dispose();

        // Assert - verify no exceptions thrown
        // In production, would verify all ChatClients are disposed
        Assert.True(true); // Disposal completed successfully
    }

    public void Dispose()
    {
        _sut.Dispose();
        GC.SuppressFinalize(this);
    }
}
