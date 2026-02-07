
namespace InfernalHierarchy.Tools.Tools.Search;

/// <summary>
/// Unified web search tool with automatic fallback
/// Tries SearXNG first, falls back to Brave Search API if unavailable
/// </summary>
public class WebSearchTool : IWebSearchTool
{
    private readonly SearXNGSearchTool _searxng;
    private readonly BraveSearchTool _brave;
    private readonly ILogger<WebSearchTool> _logger;

    public string Name => "web_search";
    public string Description => "Search the web for real-time information. Automatically uses best available search provider.";

    public WebSearchTool(
        SearXNGSearchTool searxng,
        BraveSearchTool brave,
        ILogger<WebSearchTool> logger)
    {
        _searxng = searxng;
        _brave = brave;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        // Try SearXNG first
        _logger.LogDebug("Attempting web search with SearXNG");
        var result = await _searxng.ExecuteAsync(parameters, ct);

        if (result.Success)
        {
            _logger.LogInformation("✅ Search successful via SearXNG");
            return result;
        }

        // Fallback to Brave Search
        _logger.LogWarning("SearXNG unavailable, falling back to Brave Search: {Error}", result.Error);

        result = await _brave.ExecuteAsync(parameters, ct);

        if (result.Success)
        {
            _logger.LogInformation("✅ Search successful via Brave Search (fallback)");
        }
        else
        {
            _logger.LogError("❌ All search providers failed");
        }

        return result;
    }
}
