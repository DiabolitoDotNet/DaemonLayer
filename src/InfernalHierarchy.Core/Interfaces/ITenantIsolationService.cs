
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Service for managing multi-tenancy and data isolation
/// </summary>
public interface ITenantIsolationService
{
    /// <summary>
    /// Gets the current tenant context
    /// </summary>
    /// <returns>Current tenant context or null if no tenant</returns>
    TenantContext? GetCurrentTenant();

    /// <summary>
    /// Sets the current tenant context for the operation scope
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ct">Cancellation token</param>
    Task SetCurrentTenantAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets tenant by identifier
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tenant context or null if not found</returns>
    Task<TenantContext?> GetTenantAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new tenant
    /// </summary>
    /// <param name="tenant">Tenant to create</param>
    /// <param name="ct">Cancellation token</param>
    Task CreateTenantAsync(TenantContext tenant, CancellationToken ct = default);

    /// <summary>
    /// Updates tenant configuration
    /// </summary>
    /// <param name="tenant">Tenant to update</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateTenantAsync(TenantContext tenant, CancellationToken ct = default);

    /// <summary>
    /// Deletes a tenant and all associated data
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteTenantAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Validates if current tenant can perform operation
    /// </summary>
    /// <param name="operation">Operation name</param>
    /// <param name="resourceCount">Number of resources being used</param>
    /// <returns>True if operation allowed, false otherwise</returns>
    bool CanPerformOperation(string operation, int resourceCount = 1);

    /// <summary>
    /// Gets all active tenants
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of active tenants</returns>
    Task<List<TenantContext>> GetActiveTenantsAsync(CancellationToken ct = default);

    /// <summary>
    /// Isolates data access to current tenant's data store
    /// </summary>
    /// <param name="action">Action to perform with isolated context</param>
    /// <param name="ct">Cancellation token</param>
    Task ExecuteInTenantContextAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);
}
