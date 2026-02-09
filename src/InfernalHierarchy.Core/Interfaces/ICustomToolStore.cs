using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Storage for dynamically created tools.
/// Backed by LiteDB in the default host.
/// </summary>
public interface ICustomToolStore
{
    Task UpsertAsync(CustomToolDefinition tool, CancellationToken ct = default);

    Task<CustomToolDefinition?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<CustomToolDefinition?> GetByNameAsync(string toolName, CancellationToken ct = default);

    Task<IReadOnlyList<CustomToolDefinition>> GetAllAsync(CancellationToken ct = default);
}
