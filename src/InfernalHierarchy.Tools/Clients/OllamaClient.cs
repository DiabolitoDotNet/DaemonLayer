using Azure.AI.OpenAI;
using Azure.Core;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.ClientModel;

namespace InfernalHierarchy.Tools.Clients;

/// <summary>
/// Client for Ollama LLM via Azure.AI.OpenAI (OpenAI-compatible endpoint)
/// </summary>
public class OllamaClient : ILlmClient
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<OllamaClient> _logger;
    private readonly OllamaOptions _options;

    public OllamaClient(IOptions<OllamaOptions> options, ILogger<OllamaClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Use Azure.AI.OpenAI with custom endpoint
        var clientOptions = new AzureOpenAIClientOptions();

        // Create a fake API key credential (Ollama doesn't need it but the client requires it)
        var apiKey = new ApiKeyCredential("ollama-local");

        var endpoint = _options.BaseUrl;
        var azureClient = new AzureOpenAIClient(endpoint, apiKey, clientOptions);

        _chatClient = azureClient.GetChatClient(_options.DefaultModel);

        _logger.LogInformation("🧠 Ollama client initialized: {BaseUrl} with model {Model}",
            _options.BaseUrl.ToString(), _options.DefaultModel);
    }

    /// <summary>
    /// Send a chat completion request with optional tools
    /// </summary>
    public async Task<string> GetCompletionAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = (float)_options.Temperature,
            MaxOutputTokenCount = _options.MaxTokens
        };

        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, options, ct);
            var content = response.Value.Content[0].Text;

            _logger.LogDebug("LLM Response length: {Length} chars", content.Length);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get LLM completion");
            throw;
        }
    }

    /// <summary>
    /// Send a simple one-shot completion
    /// </summary>
    public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
    {
        return GetCompletionAsync("You are a helpful AI assistant.", prompt, ct);
    }
}
