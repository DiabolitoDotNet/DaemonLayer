namespace InfernalHierarchy.Tools.Options;

/// <summary>
/// Configuration options for Ollama LLM client
/// </summary>
public class OllamaOptions
{
    public Uri BaseUrl { get; set; } = new Uri("http://localhost:11434/v1");
    public string DefaultModel { get; set; } = "qwen3:14b";
    public string AlternativeModel { get; set; } = "qwen3:14b";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
}

/// <summary>
/// Configuration options for Brave Search API
/// </summary>
public class BraveSearchOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
