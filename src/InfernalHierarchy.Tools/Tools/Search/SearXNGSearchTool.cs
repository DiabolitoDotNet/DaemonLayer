using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Clients.Search;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

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

        if (!TryGetString(parameters, "query", out var query) &&
            !TryGetString(parameters, "q", out query))
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
            _logger.LogInformation("🔍 Searching: {Query}", query);

            var searchResult = await _client.SearchAsync(query, count, ct).ConfigureAwait(false);
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

    private static bool TryGetString(Dictionary<string, object> parameters, string key, out string value)
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
            value = el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? string.Empty,
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => el.GetRawText()
            };

            return !string.IsNullOrWhiteSpace(value);
        }

        value = obj.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
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
