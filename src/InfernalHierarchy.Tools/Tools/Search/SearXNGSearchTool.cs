using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Clients.Search;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Tools.Tools.Search;

/// <summary>
/// Web search tool using SearXNG local instance
/// </summary>
public class SearXNGSearchTool : IWebSearchTool
{
    private readonly ISearXngClient _client;
    private readonly ILogger<SearXNGSearchTool> _logger;
    private readonly SearXNGOptions _options;

    public string Name => "web_search";
    public string Description => "Search the web using SearXNG for real-time information. Returns top search results.";

    public SearXNGSearchTool(
        ISearXngClient client,
        IOptions<SearXNGOptions> options,
        ILogger<SearXNGSearchTool> logger)
    {
        _client = client;
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
            _logger.LogInformation("🔍 Searching: {Query}", query);

            var searchResult = await _client.SearchAsync(query, count: 5, ct).ConfigureAwait(false);
            if (searchResult.Error != null)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = $"Search failed: {searchResult.Error}"
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

            // Format top 5 results
            var results = searchResult.Results.Take(5).Select(r =>
                $"Title: {r.Title}\nURL: {r.Url}\nSnippet: {r.Snippet}");

            var output = string.Join("\n\n---\n\n", results);

            return new ToolResult
            {
                Success = true,
                Output = output,
                Metadata = new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["result_count"] = searchResult.Results.Count
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
}
