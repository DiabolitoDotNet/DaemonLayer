using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfernalHierarchy.Tools.Clients;

/// <summary>
/// Client for Ollama LLM via its OpenAI-compatible HTTP API.
/// </summary>
public class OllamaClient : ILlmClient
    , IModelOverrideLlmClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;
    private readonly ILogger<OllamaClient> _logger;
    private readonly OllamaOptions _options;

    public OllamaClient(IOptions<OllamaOptions> options, ILogger<OllamaClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        // IMPORTANT: Ollama exposes an OpenAI-compatible API at:
        //   {BaseUrl}/chat/completions
        // BaseUrl should typically be http://localhost:11434/v1 (or host.docker.internal:11434/v1 in Docker).
        _http = new HttpClient
        {
            BaseAddress = NormalizeBaseUrl(_options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(120)
        };

        // Ollama does not require auth by default, but some reverse proxies might.
        // We intentionally do not set Authorization headers here.

        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _logger.LogInformation("🧠 Ollama client initialized: {BaseUrl} with model {Model}",
            _http.BaseAddress?.ToString() ?? _options.BaseUrl.ToString(),
            _options.DefaultModel);
    }

    /// <summary>
    /// Send a chat completion request with optional tools
    /// </summary>
    public async Task<string> GetCompletionAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        return await GetCompletionInternalAsync(systemPrompt, userMessage, modelOverride: null, ct).ConfigureAwait(false);
    }

    public Task<string> GetCompletionWithModelAsync(
        string systemPrompt,
        string userMessage,
        string model,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return GetCompletionAsync(systemPrompt, userMessage, ct);
        }

        return GetCompletionInternalAsync(systemPrompt, userMessage, modelOverride: model.Trim(), ct);
    }

    private async Task<string> GetCompletionInternalAsync(
        string systemPrompt,
        string userMessage,
        string? modelOverride,
        CancellationToken ct)
    {
        try
        {
            var request = new ChatCompletionRequest
            {
                Model = modelOverride ?? _options.DefaultModel,
                Temperature = _options.Temperature,
                MaxTokens = _options.MaxTokens,
                Stream = false,
                Messages = new()
                {
                    new ChatCompletionMessage { Role = "system", Content = systemPrompt },
                    new ChatCompletionMessage { Role = "user", Content = userMessage }
                }
            };

            var json = JsonSerializer.Serialize(request, _json);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync("chat/completions", content, ct).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "LLM request failed: {StatusCode} | Body: {Body}",
                    (int)response.StatusCode,
                    Truncate(responseText, 2000));

                throw new HttpRequestException(
                    $"Ollama completion failed with status {(int)response.StatusCode} ({response.StatusCode})");
            }

            var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText, _json);
            var message = parsed?.Choices?.FirstOrDefault()?.Message;
            var output = message?.Content;

            // Some "reasoning" models (e.g., deepseek-r1) may return the main text in a separate
            // `reasoning` field and keep `content` empty for part of the completion.
            if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(message?.Reasoning))
            {
                output = message!.Reasoning;
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                _logger.LogWarning("LLM response missing content. Raw: {Body}", Truncate(responseText, 2000));
                return string.Empty;
            }

            _logger.LogDebug("LLM Response length: {Length} chars", output.Length);
            return output;
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

    private static Uri NormalizeBaseUrl(Uri baseUrl)
    {
        // Ensure trailing slash so relative URIs compose correctly.
        var raw = baseUrl.ToString();
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw);
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLen) return value;
        return value.Substring(0, maxLen) + "…";
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatCompletionMessage> Messages { get; set; } = new();

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("stream")]
        public bool? Stream { get; set; }
    }

    private sealed class ChatCompletionMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatCompletionChoice>? Choices { get; set; }
    }

    private sealed class ChatCompletionChoice
    {
        [JsonPropertyName("message")]
        public ChatCompletionResponseMessage? Message { get; set; }
    }

    private sealed class ChatCompletionResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }
}
