using Azure.AI.OpenAI;
using Azure.Core;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using InfernalHierarchy.Tools.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.ClientModel;

namespace InfernalHierarchy.Tools.Clients;

public interface IChatModelClient
{
    Task<string> CompleteAsync(
        string systemPrompt,
        string userMessage,
        double temperature,
        int maxOutputTokens,
        CancellationToken ct);

    IAsyncEnumerable<string> CompleteStreamingAsync(
        string systemPrompt,
        string userMessage,
        double temperature,
        int maxOutputTokens,
        CancellationToken ct);
}

/// <summary>
/// Multi-model LLM client with automatic model selection and fallback
/// </summary>
public class MultiModelLlmClient : IDisposable
{
    private readonly Dictionary<string, IChatModelClient> _modelClients = new();
    private readonly ILogger<MultiModelLlmClient> _logger;
    private readonly LlmOptions _options;
    private readonly TokenUsageTracker _tokenTracker;

    public MultiModelLlmClient(
        IOptions<LlmOptions> options,
        TokenUsageTracker tokenTracker,
        ILogger<MultiModelLlmClient> logger)
    {
        _options = options.Value;
        _tokenTracker = tokenTracker;
        _logger = logger;

        InitializeModels();
    }

    private void InitializeModels()
    {
        foreach (var model in _options.Models)
        {
            _modelClients[model.Name] = CreateModelClient(model);

            _logger.LogInformation("🧠 Initialized LLM model: {Model} ({Complexity})",
                model.Name, model.Complexity);
        }
    }

    protected virtual IChatModelClient CreateModelClient(ModelConfig model)
    {
        var clientOptions = new AzureOpenAIClientOptions();
        var apiKey = new ApiKeyCredential(model.ApiKey ?? "ollama-local");
        var endpoint = model.BaseUrl;
        var azureClient = new AzureOpenAIClient(endpoint, apiKey, clientOptions);

        return new AzureChatModelClient(azureClient.GetChatClient(model.Name));
    }

    /// <summary>
    /// Get completion with automatic model selection based on task complexity
    /// </summary>
    public async Task<LlmResponse> GetCompletionAsync(
        string systemPrompt,
        string userMessage,
        TaskComplexity complexity = TaskComplexity.Medium,
        CancellationToken ct = default)
    {
        var selectedModel = SelectModelForComplexity(complexity);
        var fallbackModels = GetFallbackModels(selectedModel);

        Exception? lastException = null;

        // Try primary model
        try
        {
            return await ExecuteCompletionAsync(selectedModel, systemPrompt, userMessage, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary model {Model} failed, trying fallback", selectedModel.Name);
            lastException = ex;
        }

        // Try fallback models
        foreach (var fallbackModel in fallbackModels)
        {
            try
            {
                _logger.LogInformation("Falling back to model: {Model}", fallbackModel.Name);
                return await ExecuteCompletionAsync(fallbackModel, systemPrompt, userMessage, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback model {Model} failed", fallbackModel.Name);
                lastException = ex;
            }
        }

        // All models failed
        throw new Exception($"All LLM models failed. Last error: {lastException?.Message}", lastException);
    }

    /// <summary>
    /// Get streaming completion with real-time token delivery
    /// </summary>
    public async IAsyncEnumerable<string> GetStreamingCompletionAsync(
        string systemPrompt,
        string userMessage,
        TaskComplexity complexity = TaskComplexity.Medium,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = SelectModelForComplexity(complexity);

        if (!_modelClients.TryGetValue(model.Name, out var client))
        {
            throw new Exception($"Model {model.Name} not found");
        }

        var startTime = DateTime.UtcNow;
        var tokensGenerated = 0;

        await foreach (var text in client
            .CompleteStreamingAsync(systemPrompt, userMessage, model.Temperature, model.MaxTokens, ct)
            .WithCancellation(ct))
        {
            if (!string.IsNullOrEmpty(text))
            {
                tokensGenerated++;
                yield return text;
            }
        }

        var duration = DateTime.UtcNow - startTime;

        _tokenTracker.RecordUsage(new TokenUsageRecord
        {
            ModelName = model.Name,
            InputTokens = EstimateTokens(systemPrompt + userMessage),
            OutputTokens = tokensGenerated,
            Duration = duration,
            AgentId = "streaming"
        });

        _logger.LogDebug("Streaming completed: {Tokens} tokens in {Ms}ms",
            tokensGenerated, duration.TotalMilliseconds);
    }

    protected virtual async Task<LlmResponse> ExecuteCompletionAsync(
        ModelConfig model,
        string systemPrompt,
        string userMessage,
        CancellationToken ct)
    {
        if (!_modelClients.TryGetValue(model.Name, out var client))
        {
            throw new Exception($"Model {model.Name} not found");
        }

        var startTime = DateTime.UtcNow;

        var content = await client.CompleteAsync(systemPrompt, userMessage, model.Temperature, model.MaxTokens, ct);

        var duration = DateTime.UtcNow - startTime;
        var inputTokens = EstimateTokens(systemPrompt + userMessage);
        var outputTokens = EstimateTokens(content);

        _tokenTracker.RecordUsage(new TokenUsageRecord
        {
            ModelName = model.Name,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Duration = duration,
            AgentId = "unknown" // Set by caller
        });

        return new LlmResponse
        {
            Content = content,
            ModelUsed = model.Name,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Duration = duration
        };
    }

    private sealed class AzureChatModelClient : IChatModelClient
    {
        private readonly ChatClient _client;

        public AzureChatModelClient(ChatClient client)
        {
            _client = client;
        }

        public async Task<string> CompleteAsync(
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxOutputTokens,
            CancellationToken ct)
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userMessage)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = (float)temperature,
                MaxOutputTokenCount = maxOutputTokens
            };

            var response = await _client.CompleteChatAsync(messages, options, ct);
            return response.Value.Content.Count > 0
                ? response.Value.Content[0].Text
                : string.Empty;
        }

        public async IAsyncEnumerable<string> CompleteStreamingAsync(
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxOutputTokens,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userMessage)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = (float)temperature,
                MaxOutputTokenCount = maxOutputTokens
            };

            var streamingResponse = _client.CompleteChatStreamingAsync(messages, options, ct);

            await foreach (var chunk in streamingResponse.WithCancellation(ct))
            {
                foreach (var contentPart in chunk.ContentUpdate)
                {
                    var text = contentPart.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return text;
                    }
                }
            }
        }
    }

    private ModelConfig SelectModelForComplexity(TaskComplexity complexity)
    {
        var candidates = _options.Models
            .Where(m => m.Complexity == complexity)
            .OrderBy(m => m.Priority)
            .ToList();

        if (candidates.Any())
        {
            return candidates.First();
        }

        // Fallback to medium if exact match not found
        return _options.Models.OrderBy(m => m.Priority).First();
    }

    private List<ModelConfig> GetFallbackModels(ModelConfig primaryModel)
    {
        return _options.Models
            .Where(m => m.Name != primaryModel.Name)
            .OrderBy(m => m.Priority)
            .ToList();
    }

    private int EstimateTokens(string text)
    {
        // Rough estimation: ~4 characters per token
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Get list of all available models
    /// </summary>
    public List<ModelConfig> GetAvailableModels()
    {
        return _options.Models.OrderBy(m => m.Priority).ToList();
    }

    public void Dispose()
    {
        // ChatClients are managed by AzureOpenAIClient
        _modelClients.Clear();
    }
}

public class LlmOptions
{
    public List<ModelConfig> Models { get; set; } = new();
}

public class ModelConfig
{
    public string Name { get; set; } = string.Empty;
    public Uri BaseUrl { get; set; } = new Uri("http://localhost:11434/v1");
    public string? ApiKey { get; set; }
    public TaskComplexity Complexity { get; set; } = TaskComplexity.Medium;
    public int Priority { get; set; } = 10;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
}

public enum TaskComplexity
{
    Simple,   // Quick responses, simple tasks (gemma:2b, mistral:7b)
    Medium,   // Standard reasoning (llama3.1:8b, mixtral:8x7b)
    Complex,  // Deep reasoning, long context (llama3.1:70b, qwen:32b)
    Expert    // Specialized models (deepseek-coder, wizardlm)
}

public class LlmResponse
{
    public string Content { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public TimeSpan Duration { get; set; }
}
