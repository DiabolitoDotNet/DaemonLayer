using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Persists short-lived cached tool execution results.
/// </summary>
public interface IToolResultCacheStore
{
    /// <summary>
    /// Gets a cached result by its stable input key.
    /// </summary>
    Task<CachedToolResult?> GetAsync(string inputKey, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a cached result.
    /// </summary>
    Task UpsertAsync(CachedToolResult entry, CancellationToken ct = default);

    /// <summary>
    /// Removes a cached entry.
    /// </summary>
    Task<bool> RemoveAsync(string inputKey, CancellationToken ct = default);

    /// <summary>
    /// Removes all expired entries and returns the number removed.
    /// </summary>
    Task<int> PruneExpiredAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Clears all cached tool entries.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);
}
