using System.Text.Json;

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

        if (!TryGetQueryString(parameters, "query", out var query) &&
            !TryGetQueryString(parameters, "q", out query))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: query"
            };
        }

        var count = 5;
        if (TryGetInt(parameters, "count", out var parsedCount) ||
            TryGetInt(parameters, "num_results", out parsedCount) ||
            TryGetInt(parameters, "limit", out parsedCount))
        {
            count = Math.Clamp(parsedCount, 1, 10);
        }

        try
        {
            _logger.LogInformation("🔍 Brave Search: {Query}", query);

            var searchResult = await _client.SearchAsync(query, count, ct).ConfigureAwait(false);
            if (searchResult is null)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = "Brave Search client returned null response"
                };
            }
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

    private static bool TryGetQueryString(Dictionary<string, object> parameters, string key, out string value)
    {
        value = string.Empty;

        if (!parameters.TryGetValue(key, out var obj) || obj is null)
        {
            return false;
        }

        if (obj is string s)
        {
            value = s;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (obj is JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = el.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        // For Brave Search, enforce query is a string.
        return false;
    }

    private static bool TryGetInt(Dictionary<string, object> parameters, string key, out int value)
    {
        value = default;

        if (!parameters.TryGetValue(key, out var obj) || obj is null)
        {
            return false;
        }

        if (obj is int i)
        {
            value = i;
            return true;
        }

        if (obj is long l)
        {
            value = (int)Math.Clamp(l, int.MinValue, int.MaxValue);
            return true;
        }

        if (obj is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsed))
            {
                value = parsed;
                return true;
            }

            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out parsed))
            {
                value = parsed;
                return true;
            }
        }

        return int.TryParse(obj.ToString(), out value);
    }
}
