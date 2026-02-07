namespace InfernalHierarchy.Tools.Options;

/// <summary>
/// Controls tool result caching in the tool execution pipeline.
/// This is a pragmatic cache keyed by tool name + input signature (not semantic).
/// </summary>
public sealed class ToolResultCacheOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Default TTL applied when no per-tool override exists.
    /// Intended range: 5–30 minutes.
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// If true, clears the persisted cache on host startup ("until restart" behavior).
    /// </summary>
    public bool ClearOnStartup { get; set; }

    /// <summary>
    /// If true, allows caching unsuccessful tool results.
    /// Default is false to avoid persisting transient failures.
    /// </summary>
    public bool CacheFailures { get; set; }

    /// <summary>
    /// Tools eligible for caching by default.
    /// If empty, only tools explicitly enabled via <see cref="Tools"/> will be cached.
    /// </summary>
    public List<string> CacheableTools { get; set; } = new();

    /// <summary>
    /// Tools that must never be cached (time-dependent, user-specific, volatile).
    /// </summary>
    public List<string> NonCacheableTools { get; set; } = new();

    /// <summary>
    /// Per-tool overrides keyed by tool name.
    /// </summary>
    public Dictionary<string, ToolResultCacheOverride> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ToolResultCacheOverride
{
    /// <summary>
    /// If false, disables caching for this tool.
    /// If true, forces caching eligibility (subject to other rules).
    /// If null, falls back to global lists.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Optional TTL override for this tool.
    /// </summary>
    public TimeSpan? Ttl { get; set; }

    /// <summary>
    /// If true, always skip cache for this tool (shortcut for volatile tools).
    /// </summary>
    public bool Volatile { get; set; }
}
