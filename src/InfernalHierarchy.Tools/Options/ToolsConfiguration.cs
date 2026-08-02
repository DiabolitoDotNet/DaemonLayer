namespace InfernalHierarchy.Tools.Options;

/// <summary>
/// Configuration options for Ollama LLM client
/// </summary>
public class OllamaOptions
{
    public Uri BaseUrl { get; set; } = new Uri("http://localhost:11434/v1");
    public string DefaultModel { get; set; } = "qwen3:8b";
    public string AlternativeModel { get; set; } = "dolphin3:8b";
    public int RequestTimeoutSeconds { get; set; } = 600;
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
    public bool EnableModelRoutingPolicy { get; set; } = false;
    public List<OllamaModelRoute> ModelRoutes { get; set; } = new();
}

public class OllamaModelRoute
{
    /// <summary>
    /// Logical task family. Use "*" as wildcard.
    /// </summary>
    public string TaskType { get; set; } = "*";

    /// <summary>
    /// If set (>0), the route applies only when request latency budget is &lt;= this value.
    /// Use 0 for task-only catch-all routes.
    /// </summary>
    public int MaxLatencyMs { get; set; } = 0;

    /// <summary>
    /// Target model name for this route.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Secondary ordering key when multiple routes match.
    /// Lower means preferred.
    /// </summary>
    public int Priority { get; set; } = 100;
}

/// <summary>
/// Configuration options for Brave Search API
/// </summary>
public class BraveSearchOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
