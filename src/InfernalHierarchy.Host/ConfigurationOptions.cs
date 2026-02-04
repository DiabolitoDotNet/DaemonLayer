using InfernalHierarchy.Tools;

namespace InfernalHierarchy.Host;

// Configuration models - OllamaOptions and BraveSearchOptions now defined in Tools project

public class TelegramOptions
{
    public string BotToken { get; set; } = string.Empty;
    public long[] AllowedUserIds { get; set; } = Array.Empty<long>();
}

public class MemoryOptions
{
    public string DatabasePath { get; set; } = "data/infernal.db";
}

public class HierarchyOptions
{
    public int MaxAgentDepth { get; set; } = 4;
    public string MainAgentName { get; set; } = "Lucifer";
    public string MainAgentPersonaPath { get; set; } = "souls/lucifer.json";
}

public class SearXNGOptions
{
    public Uri BaseUrl { get; set; } = new Uri("http://localhost:8080");
    public bool Enabled { get; set; } = true;
}
