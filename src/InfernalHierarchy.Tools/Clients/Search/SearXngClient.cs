using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace InfernalHierarchy.Tools.Clients.Search;

public sealed class SearXngClient : ISearXngClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SearXngClient> _logger;
    private readonly SearXNGOptions _options;
    private readonly GlobalExceptionHandler? _exceptionHandler;

    public SearXngClient(
        HttpClient httpClient,
        IOptions<SearXNGOptions> options,
        ILogger<SearXngClient> logger,
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
            var baseUrl = _options.BaseUrl.ToString().TrimEnd('/');
            var url = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json&language=en";

            var response = await ExecuteWithResilienceAsync(
                async token =>
                {
                    var resp = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode && IsTransientStatus(resp.StatusCode))
                    {
                        throw new HttpRequestException(
                            $"SearXNG transient response {(int)resp.StatusCode} ({resp.StatusCode})",
                            inner: null,
                            statusCode: resp.StatusCode);
                    }

                    return resp;
                },
                operationName: "searxng_search",
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "SearXNG returned {StatusCode}. Body: {Body}",
                    (int)response.StatusCode,
                    body);
                return new WebSearchResponse(Array.Empty<WebSearchResultItem>(), response.StatusCode.ToString());
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<SearXngResponse>(json, JsonDefaults.WebCaseInsensitive);

            var results = (parsed?.Results ?? Array.Empty<SearXngResult>())
                .Take(Math.Max(1, count))
                .Select(r => new WebSearchResultItem(
                    r.Title ?? string.Empty,
                    r.Url ?? string.Empty,
                    r.Content ?? string.Empty))
                .ToList();

            return new WebSearchResponse(results);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "SearXNG HTTP request failed");
            return new WebSearchResponse(Array.Empty<WebSearchResultItem>(), $"Network error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse SearXNG response");
            return new WebSearchResponse(Array.Empty<WebSearchResultItem>(), "Invalid response format from SearXNG");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearXNG search failed");
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

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json when parsing SearXNG responses.")]
    private sealed class SearXngResponse
    {
        public SearXngResult[] Results { get; set; } = Array.Empty<SearXngResult>();
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json when parsing SearXNG responses.")]
    private sealed class SearXngResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
    }
}
