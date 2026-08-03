using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace InfernalHierarchy.Tools.Clients.Search;

public sealed class BraveSearchClient : IBraveSearchClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BraveSearchClient> _logger;
    private readonly BraveSearchOptions _options;
    private readonly GlobalExceptionHandler? _exceptionHandler;

    public BraveSearchClient(
        HttpClient httpClient,
        IOptions<BraveSearchOptions> options,
        ILogger<BraveSearchClient> logger,
        GlobalExceptionHandler? exceptionHandler = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _exceptionHandler = exceptionHandler;
    }

    public async Task<WebSearchResponse> SearchAsync(string query, int count, CancellationToken ct = default)
    {
        try
        {
            var safeCount = Math.Clamp(count, 1, 20);
            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={safeCount}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Subscription-Token", _options.ApiKey);
            request.Headers.Add("Accept", "application/json");

            var response = await ExecuteWithResilienceAsync(
                async token =>
                {
                    var resp = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode && IsTransientStatus(resp.StatusCode))
                    {
                        throw new HttpRequestException(
                            $"Brave Search transient response {(int)resp.StatusCode} ({resp.StatusCode})",
                            inner: null,
                            statusCode: resp.StatusCode);
                    }

                    return resp;
                },
                operationName: "brave_search",
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Brave Search returned {StatusCode}. Body: {Body}",
                    (int)response.StatusCode,
                    body);
                return new WebSearchResponse(Array.Empty<WebSearchResultItem>(), response.StatusCode.ToString());
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<BraveSearchResponse>(json);

            var results = (parsed?.Web?.Results ?? Array.Empty<BraveResult>())
                .Take(safeCount)
                .Select(r => new WebSearchResultItem(
                    r.Title ?? string.Empty,
                    r.Url ?? string.Empty,
                    r.Description ?? string.Empty))
                .ToList();

            return new WebSearchResponse(results);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Brave Search HTTP request failed");
            return new WebSearchResponse(Array.Empty<WebSearchResultItem>(), $"Network error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Brave Search response");
            return new WebSearchResponse(Array.Empty<WebSearchResultItem>(), "Invalid response format from Brave Search");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Brave Search failed");
            return new WebSearchResponse(Array.Empty<WebSearchResultItem>(), ex.Message);
        }
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

    private static bool IsTransientStatus(System.Net.HttpStatusCode status)
    {
        var code = (int)status;
        return code >= 500 || code == 429 || code == 408;
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json when parsing Brave search responses.")]
    private sealed class BraveSearchResponse
    {
        [JsonPropertyName("web")]
        public WebResults? Web { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json when parsing Brave search responses.")]
    private sealed class WebResults
    {
        [JsonPropertyName("results")]
        public BraveResult[] Results { get; set; } = Array.Empty<BraveResult>();
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json when parsing Brave search responses.")]
    private sealed class BraveResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
