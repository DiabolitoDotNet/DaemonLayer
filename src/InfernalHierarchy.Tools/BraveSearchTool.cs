using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfernalHierarchy.Tools;

/// <summary>
/// Web search tool using Brave Search API
/// Serves as fallback when SearXNG is unavailable
/// </summary>
public class BraveSearchTool : IWebSearchTool
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BraveSearchTool> _logger;
    private readonly BraveSearchOptions _options;

    public string Name => "brave_search";
    public string Description => "Search the web using Brave Search API. High-quality results with privacy focus.";

    public BraveSearchTool(
        HttpClient httpClient,
        IOptions<BraveSearchOptions> options,
        ILogger<BraveSearchTool> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(_options.ApiKey))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Brave Search is disabled or API key not configured"
            };
        }

        if (!parameters.TryGetValue("query", out var queryObj) || queryObj is not string query)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: query"
            };
        }

        try
        {
            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count=5";

            _logger.LogInformation("🔍 Brave Search: {Query}", query);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Subscription-Token", _options.ApiKey);
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Brave Search API error: {StatusCode} - {Error}", response.StatusCode, errorContent);

                return new ToolResult
                {
                    Success = false,
                    Error = $"Brave Search API returned {response.StatusCode}"
                };
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var searchResult = JsonSerializer.Deserialize<BraveSearchResponse>(json);

            if (searchResult?.Web?.Results == null || searchResult.Web.Results.Length == 0)
            {
                return new ToolResult
                {
                    Success = true,
                    Output = "No results found."
                };
            }

            // Format results
            var results = searchResult.Web.Results.Select(r =>
                $"Title: {r.Title}\nURL: {r.Url}\nDescription: {r.Description}");

            var output = string.Join("\n\n---\n\n", results);

            _logger.LogInformation("✅ Found {Count} results", searchResult.Web.Results.Length);

            return new ToolResult
            {
                Success = true,
                Output = output
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Brave Search HTTP request failed");
            return new ToolResult
            {
                Success = false,
                Error = $"Network error: {ex.Message}"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Brave Search response");
            return new ToolResult
            {
                Success = false,
                Error = "Invalid response format from Brave Search"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Brave Search failed");
            return new ToolResult
            {
                Success = false,
                Error = $"Search failed: {ex.Message}"
            };
        }
    }
}

// Response models
internal class BraveSearchResponse
{
    [JsonPropertyName("web")]
    public WebResults? Web { get; set; }
}

internal class WebResults
{
    [JsonPropertyName("results")]
    public BraveResult[] Results { get; set; } = Array.Empty<BraveResult>();
}

internal class BraveResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("age")]
    public string? Age { get; set; }

    [JsonPropertyName("page_age")]
    public string? PageAge { get; set; }
}
