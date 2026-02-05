using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Clients.Search;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Tools.Tools.Search;

/// <summary>
/// Web search tool using Brave Search API
/// Serves as fallback when SearXNG is unavailable
/// </summary>
public class BraveSearchTool : IWebSearchTool
{
    private readonly IBraveSearchClient _client;
    private readonly ILogger<BraveSearchTool> _logger;
    private readonly BraveSearchOptions _options;

    public string Name => "brave_search";
    public string Description => "Search the web using Brave Search API. High-quality results with privacy focus.";

    public BraveSearchTool(
        IBraveSearchClient client,
        IOptions<BraveSearchOptions> options,
        ILogger<BraveSearchTool> logger)
    {
        _client = client;
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
            _logger.LogInformation("🔍 Brave Search: {Query}", query);

            var searchResult = await _client.SearchAsync(query, count: 5, ct).ConfigureAwait(false);
            if (searchResult.Error != null)
            {
                if (searchResult.Error.StartsWith("Network error:", StringComparison.OrdinalIgnoreCase) ||
                    searchResult.Error.StartsWith("Invalid response format", StringComparison.OrdinalIgnoreCase))
                {
                    return new ToolResult
                    {
                        Success = false,
                        Error = searchResult.Error
                    };
                }

                return new ToolResult
                {
                    Success = false,
                    Error = $"Brave Search API returned {searchResult.Error}"
                };
            }

            if (searchResult.Results.Count == 0)
            {
                return new ToolResult
                {
                    Success = true,
                    Output = "No results found."
                };
            }

            // Format results
            var results = searchResult.Results.Select(r =>
                $"Title: {r.Title}\nURL: {r.Url}\nDescription: {r.Snippet}");

            var output = string.Join("\n\n---\n\n", results);

            _logger.LogInformation("✅ Found {Count} results", searchResult.Results.Count);

            return new ToolResult
            {
                Success = true,
                Output = output
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
