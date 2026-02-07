using System.Text.Json;

namespace InfernalHierarchy.Tools.Clients.Search;

public sealed class SearXngClient : ISearXngClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SearXngClient> _logger;
    private readonly SearXNGOptions _options;

    public SearXngClient(
        HttpClient httpClient,
        IOptions<SearXNGOptions> options,
        ILogger<SearXngClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WebSearchResponse> SearchAsync(string query, int count, CancellationToken ct = default)
    {
        try
        {
            var baseUrl = _options.BaseUrl.ToString().TrimEnd('/');
            var url = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json&language=en";

            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
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

    private sealed class SearXngResponse
    {
        public SearXngResult[] Results { get; set; } = Array.Empty<SearXngResult>();
    }

    private sealed class SearXngResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
    }
}
