using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using InfernalHierarchy.Core.Serialization;

namespace InfernalHierarchy.Tools.Tools.Search;

/// <summary>
/// Web search tool using SearXNG local instance
/// </summary>
public class SearXNGSearchTool : IWebSearchTool
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SearXNGSearchTool> _logger;
    private readonly SearXNGOptions _options;

    public string Name => "web_search";
    public string Description => "Search the web using SearXNG for real-time information. Returns top search results.";

    public SearXNGSearchTool(
        HttpClient httpClient,
        IOptions<SearXNGOptions> options,
        ILogger<SearXNGSearchTool> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new ToolResult
            {
                Success = false,
                Error = "SearXNG is disabled in configuration"
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
            var url = $"{_options.BaseUrl.ToString().TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=json&language=en";

            _logger.LogInformation("🔍 Searching: {Query}", query);

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var searchResult = JsonSerializer.Deserialize<SearXNGResponse>(
                json,
                JsonDefaults.WebCaseInsensitive);

            if (searchResult?.Results == null || searchResult.Results.Length == 0)
            {
                return new ToolResult
                {
                    Success = true,
                    Output = "No results found."
                };
            }

            // Format top 5 results
            var results = searchResult.Results.Take(5).Select(r =>
                $"Title: {r.Title}\nURL: {r.Url}\nSnippet: {r.Content}");

            var output = string.Join("\n\n---\n\n", results);

            return new ToolResult
            {
                Success = true,
                Output = output,
                Metadata = new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["result_count"] = searchResult.Results.Length
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web search failed for query: {Query}", query);
            return new ToolResult
            {
                Success = false,
                Error = $"Search failed: {ex.Message}"
            };
        }
    }

    private class SearXNGResponse
    {
        public SearXNGResult[] Results { get; set; } = Array.Empty<SearXNGResult>();
    }

    private class SearXNGResult
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
