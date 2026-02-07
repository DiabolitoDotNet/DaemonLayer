namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Cached <see cref="InfernalHierarchy.Core.Interfaces.ToolResult"/> persisted for a tool + input signature.
/// Intended for short-lived caching of expensive tool calls (web search, heavy computation, etc.).
/// </summary>
public sealed class CachedToolResult
{
    /// <summary>
    /// Tool name (e.g. "web_search").
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Stable cache key, typically a hash of tool name + canonicalized parameters.
    /// Used as the primary identifier.
    /// </summary>
    public string InputKey { get; set; } = string.Empty;

    /// <summary>
    /// Serialized <see cref="InfernalHierarchy.Core.Interfaces.ToolResult"/>.
    /// </summary>
    public string ResultJson { get; set; } = string.Empty;

    /// <summary>
    /// Expiration time (UTC). Entries past this timestamp should be treated as invalid.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Optional manual invalidation token.
    /// </summary>
    public string? ETag { get; set; }
}
