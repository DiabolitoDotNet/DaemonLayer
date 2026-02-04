// Placeholder classes referenced in Program.cs - will be implemented in respective projects

// InfernalHierarchy.Core
public class OllamaOptions
{
    public Uri BaseUrl { get; set; } = new Uri("http://localhost:11434/v1");
    public string DefaultModel { get; set; } = string.Empty;
    public string AlternativeModel { get; set; } = string.Empty;
    public int MaxTokens { get; set; }
    public double Temperature { get; set; }
}

public class TelegramOptions
{
    public string BotToken { get; set; } = string.Empty;
    public long[] AllowedUserIds { get; set; } = Array.Empty<long>();
}

public class MemoryOptions
{
    public string DatabasePath { get; set; } = string.Empty;
}

public class HierarchyOptions
{
    public int MaxAgentDepth { get; set; }
    public string MainAgentName { get; set; } = string.Empty;
    public string MainAgentPersonaPath { get; set; } = string.Empty;
}

public class SearXNGOptions
{
    public Uri BaseUrl { get; set; } = new Uri("http://localhost:8080");
    public bool Enabled { get; set; }
}

public class BraveSearchOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
