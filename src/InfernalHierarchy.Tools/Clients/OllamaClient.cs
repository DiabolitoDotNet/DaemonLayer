using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace InfernalHierarchy.Tools.Clients;

/// <summary>
/// Client for Ollama LLM via its OpenAI-compatible HTTP API.
/// </summary>
public class OllamaClient : ILlmClient
    , IModelOverrideLlmClient
    , IStreamingLlmClient
    , ITunableLlmClient
{
    private readonly JsonSerializerOptions _json;
    private readonly ILogger<OllamaClient> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOptionsMonitor<OllamaOptions>? _optionsMonitor;
    private readonly OllamaOptions? _staticOptions;

    public OllamaClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<OllamaOptions> options, ILogger<OllamaClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = options;
        _logger = logger;

        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        LogCurrentConfiguration(options.CurrentValue);
    }

    public OllamaClient(IOptions<OllamaOptions> options, ILogger<OllamaClient> logger)
    {
        _staticOptions = options.Value;
        _logger = logger;

        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        LogCurrentConfiguration(_staticOptions);
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

    public Task<string> GetCompletionWithOptionsAsync(
        string systemPrompt,
        string userMessage,
        double? temperature,
        int? maxTokens,
        CancellationToken ct = default)
    {
        return GetCompletionInternalAsync(systemPrompt, userMessage, modelOverride: null, temperature, maxTokens, ct);
    }

    public async IAsyncEnumerable<string> GetStreamingCompletionAsync(
        string systemPrompt,
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in GetStreamingCompletionInternalAsync(systemPrompt, userMessage, modelOverride: null, ct).WithCancellation(ct))
        {
            yield return chunk;
        }
    }

    public async IAsyncEnumerable<string> GetStreamingCompletionWithOptionsAsync(
        string systemPrompt,
        string userMessage,
        double? temperature,
        int? maxTokens,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in GetStreamingCompletionInternalAsync(systemPrompt, userMessage, modelOverride: null, temperature, maxTokens, ct).WithCancellation(ct))
        {
            yield return chunk;
        }
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

    private IAsyncEnumerable<string> GetStreamingCompletionInternalAsync(
        string systemPrompt,
        string userMessage,
        string? modelOverride,
        CancellationToken ct)
        => GetStreamingCompletionInternalAsync(systemPrompt, userMessage, modelOverride, temperature: null, maxTokens: null, ct);

    private async IAsyncEnumerable<string> GetStreamingCompletionInternalAsync(
        string systemPrompt,
        string userMessage,
        string? modelOverride,
        double? temperature,
        int? maxTokens,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var options = GetCurrentOptions();
        using var http = CreateConfiguredHttpClient(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var payload = new ChatCompletionRequest
        {
            Model = modelOverride ?? options.DefaultModel,
            Temperature = temperature ?? options.Temperature,
            MaxTokens = maxTokens ?? options.MaxTokens,
            Stream = true,
            Messages = new()
            {
                new ChatCompletionMessage { Role = "system", Content = systemPrompt },
                new ChatCompletionMessage { Role = "user", Content = userMessage }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogError(
                    "LLM streaming request failed: {StatusCode} | Body: {Body}",
                    (int)response.StatusCode,
                    Truncate(body, 2000));
                throw new HttpRequestException(
                    $"Ollama streaming completion failed with status {(int)response.StatusCode} ({response.StatusCode})");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line.Substring("data:".Length).Trim();
                if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                string? content = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);

                    if (doc.RootElement.TryGetProperty("error", out var errorEl))
                    {
                        _logger.LogWarning("LLM streaming error payload: {Error}", Truncate(errorEl.ToString(), 2000));
                        continue;
                    }

                    if (doc.RootElement.TryGetProperty("choices", out var choicesEl) &&
                        choicesEl.ValueKind == JsonValueKind.Array &&
                        choicesEl.GetArrayLength() > 0)
                    {
                        var choice0 = choicesEl[0];

                        // OpenAI streaming format: choices[0].delta.content
                        if (choice0.TryGetProperty("delta", out var deltaEl) &&
                            deltaEl.ValueKind == JsonValueKind.Object &&
                            deltaEl.TryGetProperty("content", out var deltaContentEl) &&
                            deltaContentEl.ValueKind == JsonValueKind.String)
                        {
                            content = deltaContentEl.GetString();
                        }
                        // Fallback format: choices[0].message.content
                        else if (choice0.TryGetProperty("message", out var messageEl) &&
                            messageEl.ValueKind == JsonValueKind.Object &&
                            messageEl.TryGetProperty("content", out var msgContentEl) &&
                            msgContentEl.ValueKind == JsonValueKind.String)
                        {
                            content = msgContentEl.GetString();
                        }
                    }
                }
                catch (JsonException)
                {
                    _logger.LogDebug("Skipping non-JSON SSE data chunk: {Chunk}", Truncate(data, 500));
                }

                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                }
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<string> GetCompletionInternalAsync(
        string systemPrompt,
        string userMessage,
        string? modelOverride,
        CancellationToken ct)
        => await GetCompletionInternalAsync(systemPrompt, userMessage, modelOverride, temperature: null, maxTokens: null, ct).ConfigureAwait(false);

    private async Task<string> GetCompletionInternalAsync(
        string systemPrompt,
        string userMessage,
        string? modelOverride,
        double? temperature,
        int? maxTokens,
        CancellationToken ct)
    {
        try
        {
            var options = GetCurrentOptions();
            using var http = CreateConfiguredHttpClient(options);
            var request = new ChatCompletionRequest
            {
                Model = modelOverride ?? options.DefaultModel,
                Temperature = temperature ?? options.Temperature,
                MaxTokens = maxTokens ?? options.MaxTokens,
                Stream = false,
                Messages = new()
                {
                    new ChatCompletionMessage { Role = "system", Content = systemPrompt },
                    new ChatCompletionMessage { Role = "user", Content = userMessage }
                }
            };

            var json = JsonSerializer.Serialize(request, _json);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await http.PostAsync("chat/completions", content, ct).ConfigureAwait(false);
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

    private OllamaOptions GetCurrentOptions() => _optionsMonitor?.CurrentValue ?? _staticOptions ?? new OllamaOptions();

    private HttpClient CreateConfiguredHttpClient(OllamaOptions options)
    {
        var http = _httpClientFactory?.CreateClient(nameof(OllamaClient)) ?? new HttpClient();
        http.BaseAddress = NormalizeBaseUrl(options.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds > 0 ? options.RequestTimeoutSeconds : 120);
        return http;
    }

    private void LogCurrentConfiguration(OllamaOptions options)
    {
        var timeoutSeconds = options.RequestTimeoutSeconds > 0
            ? options.RequestTimeoutSeconds
            : 120;

        _logger.LogInformation(
            "🧠 Ollama client initialized: {BaseUrl} | model={Model} | timeout={TimeoutSeconds}s",
            NormalizeBaseUrl(options.BaseUrl).ToString(),
            options.DefaultModel,
            timeoutSeconds);
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
