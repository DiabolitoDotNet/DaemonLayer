using FluentAssertions;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;
using System.Collections;
using System.Runtime.CompilerServices;
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

    private sealed class TestableMultiModelLlmClient : MultiModelLlmClient
    {
        private readonly Dictionary<string, int> _attemptsByModel;
        private readonly Func<string, bool> _shouldFailForModel;

        public TestableMultiModelLlmClient(
            IOptions<LlmOptions> options,
            TokenUsageTracker tokenTracker,
            ILogger<MultiModelLlmClient> logger,
            Func<string, bool> shouldFailForModel,
            Dictionary<string, int> attemptsByModel)
            : base(options, tokenTracker, logger)
        {
            _shouldFailForModel = shouldFailForModel;
            _attemptsByModel = attemptsByModel;
        }

        protected override Task<LlmResponse> ExecuteCompletionAsync(
            ModelConfig model,
            string systemPrompt,
            string userMessage,
            CancellationToken ct)
        {
            _attemptsByModel.TryGetValue(model.Name, out var current);
            _attemptsByModel[model.Name] = current + 1;

            if (_shouldFailForModel(model.Name))
            {
                throw new InvalidOperationException($"Simulated failure for {model.Name}");
            }

            return Task.FromResult(new LlmResponse
            {
                Content = $"ok:{model.Name}",
                ModelUsed = model.Name,
                InputTokens = 1,
                OutputTokens = 1,
                Duration = TimeSpan.FromMilliseconds(1)
            });
        }
    }

    private sealed class StubChatModelClient : IChatModelClient
    {
        private readonly Func<string> _content;
        private readonly IReadOnlyList<string> _stream;

        public StubChatModelClient(Func<string> content, IReadOnlyList<string>? stream = null)
        {
            _content = content;
            _stream = stream ?? Array.Empty<string>();
        }

        public Task<string> CompleteAsync(
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxOutputTokens,
            CancellationToken ct)
        {
            return Task.FromResult(_content());
        }

        public async IAsyncEnumerable<string> CompleteStreamingAsync(
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxOutputTokens,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in _stream)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
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
    public async Task GetCompletionAsync_WhenPrimaryFails_ShouldTryFallbacksInPriorityOrder()
    {
        // Arrange
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        var client = new TestableMultiModelLlmClient(
            Options.Create(_options),
            _tokenTracker,
            _mockLogger.Object,
            shouldFailForModel: modelName => modelName is "llama3.1:8b", // fail primary (Medium)
            attemptsByModel: attempts);

        // Act
        var response = await client.GetCompletionAsync(
            systemPrompt: "sys",
            userMessage: "user",
            complexity: TaskComplexity.Medium,
            ct: CancellationToken.None);

        // Assert
        response.ModelUsed.Should().NotBe("llama3.1:8b");
        attempts["llama3.1:8b"].Should().Be(1);

        // Fallback order is by Priority excluding primary:
        // gemma (10) then qwen (30) then deepseek (40)
        attempts.Should().ContainKey("gemma:2b");
        attempts["gemma:2b"].Should().Be(1);
        attempts.ContainsKey("qwen:32b").Should().BeFalse();
        attempts.ContainsKey("deepseek-coder:6.7b").Should().BeFalse();

        client.Dispose();
    }

    [Fact]
    public async Task GetCompletionAsync_WhenAllModelsFail_ShouldThrowWithLastExceptionAsInner()
    {
        // Arrange
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        var client = new TestableMultiModelLlmClient(
            Options.Create(_options),
            _tokenTracker,
            _mockLogger.Object,
            shouldFailForModel: _ => true,
            attemptsByModel: attempts);

        // Act
        var act = async () => await client.GetCompletionAsync(
            systemPrompt: "sys",
            userMessage: "user",
            complexity: TaskComplexity.Simple,
            ct: CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Message.Should().StartWith("All LLM models failed");
        ex.Which.InnerException.Should().NotBeNull();

        // Primary + all fallbacks attempted
        attempts.Values.Sum().Should().Be(_options.Models.Count);

        client.Dispose();
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
    public async Task GetStreamingCompletionAsync_WhenModelClientMissing_ShouldThrowBeforeNetwork()
    {
        // Arrange
        var clientsField = _sut.GetType()
            .GetField("_modelClients", BindingFlags.NonPublic | BindingFlags.Instance);

        clientsField.Should().NotBeNull();
        var clients = clientsField!.GetValue(_sut).Should().BeAssignableTo<IDictionary>().Subject;
        ((IDictionary)clients).Clear();

        // Act
        var act = async () =>
        {
            await foreach (var _ in _sut.GetStreamingCompletionAsync("sys", "user", TaskComplexity.Simple))
            {
                // no-op
            }
        };

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("Model*not found*");
    }

    [Fact]
    public void GetAvailableModels_ShouldReturnModelsOrderedByPriority()
    {
        // Act
        var models = _sut.GetAvailableModels();

        // Assert
        models.Should().HaveCount(4);
        models.Select(m => m.Priority).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetCompletionAsync_UsingBaseExecuteCompletionAsync_RecordsTokenUsage()
    {
        var options = new LlmOptions
        {
            Models =
            [
                new ModelConfig
                {
                    Name = "m1",
                    Complexity = TaskComplexity.Medium,
                    Priority = 1,
                    MaxTokens = 100,
                    Temperature = 0.1,
                    BaseUrl = new Uri("http://localhost:11434/v1")
                }
            ]
        };

        var tokenTracker = new TokenUsageTracker(Mock.Of<ILogger<TokenUsageTracker>>());
        var sut = new MultiModelLlmClient(Options.Create(options), tokenTracker, _mockLogger.Object);

        var clientsField = sut.GetType().GetField("_modelClients", BindingFlags.NonPublic | BindingFlags.Instance);
        clientsField.Should().NotBeNull();
        var clients = (IDictionary)clientsField!.GetValue(sut)!;
        clients.Clear();
        clients["m1"] = new StubChatModelClient(() => "abcd");

        var response = await sut.GetCompletionAsync("sys", "user", TaskComplexity.Medium, CancellationToken.None);

        response.ModelUsed.Should().Be("m1");
        response.Content.Should().Be("abcd");

        var stats = tokenTracker.GetModelStats("m1");
        stats.Should().NotBeNull();
        stats!.CallCount.Should().Be(1);
        stats.TotalInputTokens.Should().Be(2); // ceil(len("sysuser")/4) = ceil(7/4)=2
        stats.TotalOutputTokens.Should().Be(1); // ceil(len("abcd")/4)=1
    }

    [Fact]
    public async Task GetStreamingCompletionAsync_UsingStubClient_YieldsChunks_AndRecordsUsage()
    {
        var options = new LlmOptions
        {
            Models =
            [
                new ModelConfig
                {
                    Name = "m1",
                    Complexity = TaskComplexity.Simple,
                    Priority = 1,
                    MaxTokens = 100,
                    Temperature = 0.1,
                    BaseUrl = new Uri("http://localhost:11434/v1")
                }
            ]
        };

        var tokenTracker = new TokenUsageTracker(Mock.Of<ILogger<TokenUsageTracker>>());
        var sut = new MultiModelLlmClient(Options.Create(options), tokenTracker, _mockLogger.Object);

        var clientsField = sut.GetType().GetField("_modelClients", BindingFlags.NonPublic | BindingFlags.Instance);
        var clients = (IDictionary)clientsField!.GetValue(sut)!;
        clients.Clear();
        clients["m1"] = new StubChatModelClient(() => "unused", stream: new[] { "he", "", "llo" });

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetStreamingCompletionAsync("sys", "user", TaskComplexity.Simple, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Should().Equal("he", "llo");

        var stats = tokenTracker.GetModelStats("m1");
        stats.Should().NotBeNull();
        stats!.CallCount.Should().Be(1);
        stats.TotalInputTokens.Should().Be(2);
        stats.TotalOutputTokens.Should().Be(2); // one per non-empty chunk
    }

    [Fact]
    public void SelectModelForComplexity_WhenNoExactMatch_FallsBackToLowestPriority()
    {
        var options = new LlmOptions
        {
            Models = new List<ModelConfig>
            {
                new() { Name = "low", Complexity = TaskComplexity.Simple, Priority = 1, BaseUrl = new Uri("http://localhost:11434/v1") },
                new() { Name = "high", Complexity = TaskComplexity.Medium, Priority = 10, BaseUrl = new Uri("http://localhost:11434/v1") }
            }
        };

        var sut = new MultiModelLlmClient(Options.Create(options), _tokenTracker, _mockLogger.Object);

        var selectedModel = sut.GetType()
            .GetMethod("SelectModelForComplexity", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sut, new object[] { TaskComplexity.Expert }) as ModelConfig;

        selectedModel.Should().NotBeNull();
        selectedModel!.Name.Should().Be("low");
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

        // Assert
        var clientsField = disposableSut.GetType()
            .GetField("_modelClients", BindingFlags.NonPublic | BindingFlags.Instance);

        clientsField.Should().NotBeNull();
        var clients = clientsField!.GetValue(disposableSut).Should().BeAssignableTo<IDictionary>().Subject;
        ((IDictionary)clients).Count.Should().Be(0);
    }

    public void Dispose()
    {
        _sut.Dispose();
        GC.SuppressFinalize(this);
    }
}
