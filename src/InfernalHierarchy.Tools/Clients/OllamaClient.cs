using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Diagnostics.CodeAnalysis;
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
    , IModelRoutingLlmClient
    , IImageLlmClient
{
    private readonly JsonSerializerOptions _json;
    private readonly ILogger<OllamaClient> _logger;
    private readonly GlobalExceptionHandler? _exceptionHandler;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOptionsMonitor<OllamaOptions>? _optionsMonitor;
    private readonly OllamaOptions? _staticOptions;
    private readonly IModelRoutingFeedbackStore? _routingFeedback;

    public OllamaClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OllamaOptions> options,
        ILogger<OllamaClient> logger,
        GlobalExceptionHandler? exceptionHandler = null,
        IModelRoutingFeedbackStore? routingFeedback = null)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = options;
        _logger = logger;
        _exceptionHandler = exceptionHandler;
        _routingFeedback = routingFeedback;

        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        LogCurrentConfiguration(options.CurrentValue);
    }

    public OllamaClient(IOptions<OllamaOptions> options, ILogger<OllamaClient> logger, GlobalExceptionHandler? exceptionHandler = null, IModelRoutingFeedbackStore? routingFeedback = null)
    {
        _staticOptions = options.Value;
        _logger = logger;
        _exceptionHandler = exceptionHandler;
        _routingFeedback = routingFeedback;

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

    public Task<string> GetCompletionWithRoutingAsync(
        string systemPrompt,
        string userMessage,
        LlmRoutingHint routingHint,
        double? temperature = null,
        int? maxTokens = null,
        CancellationToken ct = default)
    {
        var options = GetCurrentOptions();
        var selectedModel = OllamaModelRoutingPolicy.ResolveModel(options, routingHint, _routingFeedback);

        _logger.LogDebug(
            "LLM routing selected model={Model} task_type={TaskType} latency_budget_ms={LatencyBudgetMs}",
            selectedModel,
            routingHint.TaskType,
            routingHint.LatencyBudgetMs);

        return GetCompletionInternalAsync(systemPrompt, userMessage, selectedModel, temperature, maxTokens, ct);
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

    public async IAsyncEnumerable<string> GetStreamingCompletionWithRoutingAsync(
        string systemPrompt,
        string userMessage,
        LlmRoutingHint routingHint,
        double? temperature = null,
        int? maxTokens = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var options = GetCurrentOptions();
        var selectedModel = OllamaModelRoutingPolicy.ResolveModel(options, routingHint, _routingFeedback);

        _logger.LogDebug(
            "LLM streaming routing selected model={Model} task_type={TaskType} latency_budget_ms={LatencyBudgetMs}",
            selectedModel,
            routingHint.TaskType,
            routingHint.LatencyBudgetMs);

        await foreach (var chunk in GetStreamingCompletionInternalAsync(systemPrompt, userMessage, selectedModel, temperature, maxTokens, ct).WithCancellation(ct))
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
        var compactedUserMessage = CompactPromptIfEnabled(options, userMessage);
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
                new ChatCompletionMessage { Role = "user", Content = compactedUserMessage }
            }
        };

        var callStartedUtc = DateTime.UtcNow;

        request.Content = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        var completedSuccessfully = false;
        try
        {
            response = await ExecuteWithResilienceAsync(
                token => http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token),
                operationName: "ollama_streaming_completion",
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogError(
                    "LLM streaming request failed: {StatusCode} | Body: {Body}",
                    (int)response.StatusCode,
                    Truncate(body, 2000));
                throw new HttpRequestException(
                    $"Ollama streaming completion failed with status {(int)response.StatusCode} ({response.StatusCode})",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
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

            completedSuccessfully = true;
        }
        finally
        {
            _routingFeedback?.RecordOutcome(
                payload.Model,
                success: completedSuccessfully,
                duration: DateTime.UtcNow - callStartedUtc,
                outputTokens: 0);
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
        var options = GetCurrentOptions();
        var compactedUserMessage = CompactPromptIfEnabled(options, userMessage);
        var selectedModel = modelOverride ?? options.DefaultModel;
        string output;

        try
        {
            output = await ExecuteCompletionRequestAsync(
                options,
                selectedModel,
                systemPrompt,
                compactedUserMessage,
                temperature,
                maxTokens,
                ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
            when (IsPolicyBlockedHttpRequestException(ex) &&
                  TryGetAlternativeModelForFallback(options, selectedModel, modelOverride, out var fallbackForBlockedPrimary))
        {
            _logger.LogInformation(
                "Primary model {PrimaryModel} blocked by policy ({StatusCode}), retrying with alternative model {AlternativeModel}",
                selectedModel,
                ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : -1,
                fallbackForBlockedPrimary);

            output = await ExecuteCompletionRequestAsync(
                options,
                fallbackForBlockedPrimary,
                systemPrompt,
                compactedUserMessage,
                temperature,
                maxTokens,
                ct).ConfigureAwait(false);
        }

        if (TryGetAlternativeModelForFallback(options, selectedModel, modelOverride, out var alternativeModel) &&
            IsPolicyRefusalOutput(output))
        {
            _logger.LogInformation(
                "Primary model {PrimaryModel} returned policy refusal, retrying with alternative model {AlternativeModel}",
                selectedModel,
                alternativeModel);

            return await ExecuteCompletionRequestAsync(
                options,
                alternativeModel,
                systemPrompt,
                compactedUserMessage,
                temperature,
                maxTokens,
                ct).ConfigureAwait(false);
        }

        return output;
    }

    private async Task<string> ExecuteCompletionRequestAsync(
        OllamaOptions options,
        string model,
        string systemPrompt,
        string compactedUserMessage,
        double? temperature,
        int? maxTokens,
        CancellationToken ct)
    {
        var callStartedUtc = DateTime.UtcNow;

        try
        {
            using var http = CreateConfiguredHttpClient(options);
            var request = new ChatCompletionRequest
            {
                Model = model,
                Temperature = temperature ?? options.Temperature,
                MaxTokens = maxTokens ?? options.MaxTokens,
                Stream = false,
                Messages = new()
                {
                    new ChatCompletionMessage { Role = "system", Content = systemPrompt },
                    new ChatCompletionMessage { Role = "user", Content = compactedUserMessage }
                }
            };

            var json = JsonSerializer.Serialize(request, _json);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await ExecuteWithResilienceAsync(
                token => http.PostAsync("chat/completions", content, token),
                operationName: "ollama_completion",
                ct).ConfigureAwait(false);

            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "LLM request failed: {StatusCode} | Body: {Body}",
                    (int)response.StatusCode,
                    Truncate(responseText, 2000));

                if (IsPolicyBlockedHttpResponse(response.StatusCode, responseText))
                {
                    throw new HttpRequestException(
                        $"POLICY_BLOCKED: Ollama completion blocked for model {model} with status {(int)response.StatusCode} ({response.StatusCode})",
                        inner: null,
                        statusCode: response.StatusCode);
                }

                throw new HttpRequestException(
                    $"Ollama completion failed with status {(int)response.StatusCode} ({response.StatusCode})",
                    inner: null,
                    statusCode: response.StatusCode);
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
                _routingFeedback?.RecordOutcome(
                    request.Model,
                    success: true,
                    duration: DateTime.UtcNow - callStartedUtc,
                    outputTokens: 0);
                return string.Empty;
            }

            _routingFeedback?.RecordOutcome(
                request.Model,
                success: true,
                duration: DateTime.UtcNow - callStartedUtc,
                outputTokens: EstimateTokens(output));

            _logger.LogDebug("LLM Response length: {Length} chars", output.Length);
            return output;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (IsPolicyBlockedHttpRequestException(ex))
        {
            _routingFeedback?.RecordOutcome(
                model,
                success: false,
                duration: DateTime.UtcNow - callStartedUtc,
                outputTokens: 0);
            throw;
        }
        catch (Exception ex)
        {
            _routingFeedback?.RecordOutcome(
                model,
                success: false,
                duration: DateTime.UtcNow - callStartedUtc,
                outputTokens: 0);
            _logger.LogError(ex, "Failed to get LLM completion");
            throw;
        }
    }

    private static bool TryGetAlternativeModelForFallback(
        OllamaOptions options,
        string selectedModel,
        string? modelOverride,
        [NotNullWhen(true)] out string? alternativeModel)
    {
        alternativeModel = null;

        if (!string.IsNullOrWhiteSpace(modelOverride) &&
            !string.Equals(modelOverride, options.DefaultModel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(selectedModel, options.DefaultModel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.AlternativeModel))
        {
            return false;
        }

        var candidate = options.AlternativeModel.Trim();
        if (string.Equals(candidate, selectedModel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        alternativeModel = candidate;
        return true;
    }

    private static bool IsPolicyBlockedHttpResponse(HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode is not HttpStatusCode.BadRequest and not HttpStatusCode.Forbidden and not HttpStatusCode.UnprocessableEntity)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        var body = responseBody.ToLowerInvariant();
        return body.Contains("policy", StringComparison.Ordinal) ||
               body.Contains("safety", StringComparison.Ordinal) ||
               body.Contains("moderation", StringComparison.Ordinal) ||
               body.Contains("content filter", StringComparison.Ordinal) ||
               body.Contains("disallowed", StringComparison.Ordinal) ||
               body.Contains("blocked", StringComparison.Ordinal) ||
               body.Contains("refused", StringComparison.Ordinal);
    }

    private static bool IsPolicyRefusalOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        // Keep this heuristic strict to avoid rerouting normal answers.
        var normalized = output.Trim().ToLowerInvariant();
        if (normalized.Length > 800)
        {
            return false;
        }

        return normalized.Contains("i can't assist with", StringComparison.Ordinal) ||
               normalized.Contains("i cannot assist with", StringComparison.Ordinal) ||
               normalized.Contains("i can't help with", StringComparison.Ordinal) ||
               normalized.Contains("i cannot help with", StringComparison.Ordinal) ||
               normalized.Contains("i'm sorry, but i can't", StringComparison.Ordinal) ||
               normalized.Contains("i am sorry, but i can't", StringComparison.Ordinal) ||
               normalized.Contains("violates", StringComparison.Ordinal) && normalized.Contains("policy", StringComparison.Ordinal);
    }

    private static bool IsPolicyBlockedHttpRequestException(HttpRequestException ex)
    {
        return ex.Message.StartsWith("POLICY_BLOCKED:", StringComparison.Ordinal);
    }

    /// <summary>
    /// Send a simple one-shot completion
    /// </summary>
    public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
    {
        return GetCompletionAsync("You are a helpful AI assistant.", prompt, ct);
    }

    public async Task<string> GetImageCompletionAsync(
        string systemPrompt,
        string userMessage,
        byte[] imageBytes,
        string mimeType,
        string? modelOverride = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("imageBytes cannot be empty", nameof(imageBytes));
        }

        if (string.IsNullOrWhiteSpace(mimeType))
        {
            mimeType = "image/png";
        }

        var options = GetCurrentOptions();
        using var http = CreateConfiguredHttpClient(options);

        var dataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
        var request = new
        {
            model = modelOverride ?? options.DefaultModel,
            temperature = options.Temperature,
            max_tokens = options.MaxTokens,
            stream = false,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = userMessage
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = dataUrl
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(request, _json);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await ExecuteWithResilienceAsync(
            token => http.PostAsync("chat/completions", content, token),
            operationName: "ollama_image_completion",
            ct).ConfigureAwait(false);

        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "LLM image request failed: {StatusCode} | Body: {Body}",
                (int)response.StatusCode,
                Truncate(responseText, 2000));

            throw new HttpRequestException(
                $"Ollama image completion failed with status {(int)response.StatusCode} ({response.StatusCode})",
                inner: null,
                statusCode: response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText, _json);
        var message = parsed?.Choices?.FirstOrDefault()?.Message;
        var output = message?.Content;

        if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(message?.Reasoning))
        {
            output = message!.Reasoning;
        }

        return output ?? string.Empty;
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

    private string CompactPromptIfEnabled(OllamaOptions options, string userMessage)
    {
        if (!options.EnablePromptCompaction)
        {
            return userMessage;
        }

        var maxChars = Math.Max(1, options.PromptCompactionMaxChars);
        if (string.IsNullOrEmpty(userMessage) || userMessage.Length <= maxChars)
        {
            return userMessage;
        }

        var headChars = Math.Max(100, options.PromptCompactionHeadChars);
        var tailChars = Math.Max(100, options.PromptCompactionTailChars);
        if (headChars + tailChars >= userMessage.Length)
        {
            return userMessage;
        }

        var omittedChars = userMessage.Length - headChars - tailChars;
        var compacted = string.Concat(
            userMessage.AsSpan(0, headChars),
            $"\n\n[... prompt compacted: {omittedChars} chars omitted ...]\n\n",
            userMessage.AsSpan(userMessage.Length - tailChars));

        _logger.LogInformation(
            "Prompt compaction applied | original_chars={OriginalChars} compacted_chars={CompactedChars}",
            userMessage.Length,
            compacted.Length);

        return compacted;
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLen) return value;
        return value.Substring(0, maxLen) + "…";
    }

    private async Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct)
    {
        if (_exceptionHandler is null)
        {
            return await operation(ct).ConfigureAwait(false);
        }

        return await _exceptionHandler
            .ExecuteWithHandlingAsync(operation, operationName, maxRetries: 3, ct: ct)
            .ConfigureAwait(false);
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

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json during chat completion response deserialization.")]
    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatCompletionChoice>? Choices { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json during chat completion response deserialization.")]
    private sealed class ChatCompletionChoice
    {
        [JsonPropertyName("message")]
        public ChatCompletionResponseMessage? Message { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json during chat completion response deserialization.")]
    private sealed class ChatCompletionResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }

}
