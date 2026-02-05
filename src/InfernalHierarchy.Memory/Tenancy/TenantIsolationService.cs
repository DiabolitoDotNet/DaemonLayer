using System.Collections.Concurrent;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Memory.Tenancy;

/// <summary>
/// Implements multi-tenant data isolation with separate LiteDB databases per tenant
/// </summary>
public class TenantIsolationService : ITenantIsolationService
{
    private readonly ILogger<TenantIsolationService> _logger;
    private readonly AsyncLocal<TenantContext?> _currentTenant = new();
    private readonly ConcurrentDictionary<string, TenantContext> _tenantCache = new();
    private readonly string _tenantsDbPath;
    private readonly string _dataRootPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantIsolationService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="dataRootPath">Root path for tenant data (default: ./data)</param>
    public TenantIsolationService(ILogger<TenantIsolationService> logger, string dataRootPath = "./data")
    {
        _logger = logger;
        _dataRootPath = dataRootPath;
        _tenantsDbPath = Path.Combine(_dataRootPath, "tenants.db");

        Directory.CreateDirectory(_dataRootPath);
        InitializeDefaultTenant();
    }

    /// <inheritdoc/>
    public TenantContext? GetCurrentTenant() => _currentTenant.Value;

    /// <inheritdoc/>
    public async Task SetCurrentTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = await GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (tenant == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} not found");
        }

        if (!tenant.IsActive)
        {
            throw new InvalidOperationException($"Tenant {tenantId} is not active");
        }

        _currentTenant.Value = tenant;
        _logger.LogDebug("Set current tenant to {TenantId} ({TenantName})", tenantId, tenant.Name);
    }

    /// <inheritdoc/>
    public async Task<TenantContext?> GetTenantAsync(string tenantId, CancellationToken ct = default)
    {
        // Check cache first
        if (_tenantCache.TryGetValue(tenantId, out var cachedTenant))
        {
            return cachedTenant;
        }

        // Load from database
        using var db = new LiteDatabase(_tenantsDbPath);
        var collection = db.GetCollection<TenantContext>("tenants");
        collection.EnsureIndex(x => x.TenantId, unique: true);

        var tenant = await Task.Run(() => collection.FindOne(x => x.TenantId == tenantId), ct).ConfigureAwait(false);

        if (tenant != null)
        {
            _tenantCache[tenantId] = tenant;
        }

        return tenant;
    }

    /// <inheritdoc/>
    public async Task CreateTenantAsync(TenantContext tenant, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating tenant {TenantId} ({TenantName}) with tier {Tier}",
            tenant.TenantId, tenant.Name, tenant.Tier);

        // Set resource limits based on tier
        SetTierLimits(tenant);

        // Create dedicated database directory
        var tenantDataPath = Path.Combine(_dataRootPath, tenant.TenantId);
        Directory.CreateDirectory(tenantDataPath);
        tenant.DatabasePath = Path.Combine(tenantDataPath, "memory.db");

        // Save to tenants database
        using var db = new LiteDatabase(_tenantsDbPath);
        var collection = db.GetCollection<TenantContext>("tenants");
        collection.EnsureIndex(x => x.TenantId, unique: true);

        await Task.Run(() => collection.Insert(tenant), ct).ConfigureAwait(false);

        // Update cache
        _tenantCache[tenant.TenantId] = tenant;

        _logger.LogInformation("Created tenant {TenantId} at {DatabasePath}", tenant.TenantId, tenant.DatabasePath);
    }

    /// <inheritdoc/>
    public async Task UpdateTenantAsync(TenantContext tenant, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating tenant {TenantId}", tenant.TenantId);

        using var db = new LiteDatabase(_tenantsDbPath);
        var collection = db.GetCollection<TenantContext>("tenants");

        // Do not rely on LiteDB's internal _id mapping for TenantContext.
        // TenantId is the logical key, so update by TenantId to avoid null _id issues.
        collection.EnsureIndex(x => x.TenantId, unique: true);

        await Task.Run(() =>
        {
            var existing = collection.FindOne(x => x.TenantId == tenant.TenantId);
            if (existing != null)
            {
                // Preserve stable identity fields unless caller explicitly sets them.
                tenant.CreatedAt = existing.CreatedAt;
                tenant.DatabasePath ??= existing.DatabasePath;

                collection.DeleteMany(x => x.TenantId == tenant.TenantId);
            }

            collection.Insert(tenant);
        }, ct).ConfigureAwait(false);

        // Update cache
        _tenantCache[tenant.TenantId] = tenant;
    }

    /// <inheritdoc/>
    public async Task DeleteTenantAsync(string tenantId, CancellationToken ct = default)
    {
        _logger.LogWarning("Deleting tenant {TenantId} and ALL associated data", tenantId);

        var tenant = await GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (tenant == null)
        {
            _logger.LogWarning("Tenant {TenantId} not found for deletion", tenantId);
            return;
        }

        // Delete tenant database and directory
        if (tenant.DatabasePath != null)
        {
            var tenantDir = Path.GetDirectoryName(tenant.DatabasePath);
            if (tenantDir != null && Directory.Exists(tenantDir))
            {
                Directory.Delete(tenantDir, recursive: true);
                _logger.LogInformation("Deleted tenant data directory: {Directory}", tenantDir);
            }
        }

        // Remove from tenants database
        using var db = new LiteDatabase(_tenantsDbPath);
        var collection = db.GetCollection<TenantContext>("tenants");
        await Task.Run(() => collection.DeleteMany(x => x.TenantId == tenantId), ct).ConfigureAwait(false);

        // Remove from cache
        _tenantCache.TryRemove(tenantId, out _);

        _logger.LogInformation("Deleted tenant {TenantId}", tenantId);
    }

    /// <inheritdoc/>
    public bool CanPerformOperation(string operation, int resourceCount = 1)
    {
        var tenant = GetCurrentTenant();
        if (tenant == null)
        {
            _logger.LogWarning("No tenant context for operation {Operation}", operation);
            return false;
        }

        if (!tenant.IsActive)
        {
            _logger.LogWarning("Tenant {TenantId} is not active for operation {Operation}",
                tenant.TenantId, operation);
            return false;
        }

        // Check resource limits based on operation type
        return operation.ToLowerInvariant() switch
        {
            "create_agent" => true, // Checked by ResourceLimitService
            "memory_write" => true, // Checked by memory service
            "llm_call" => true,     // Checked by token tracker
            _ => true
        };
    }

    /// <inheritdoc/>
    public async Task<List<TenantContext>> GetActiveTenantsAsync(CancellationToken ct = default)
    {
        using var db = new LiteDatabase(_tenantsDbPath);
        var collection = db.GetCollection<TenantContext>("tenants");

        var tenants = await Task.Run(() =>
            collection.Find(x => x.IsActive).ToList(), ct).ConfigureAwait(false);

        return tenants;
    }

    /// <inheritdoc/>
    public async Task ExecuteInTenantContextAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        var tenant = GetCurrentTenant();
        if (tenant == null)
        {
            throw new InvalidOperationException("No tenant context set");
        }

        _logger.LogDebug("Executing action in tenant context: {TenantId}", tenant.TenantId);

        try
        {
            await action(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action in tenant context {TenantId}", tenant.TenantId);
            throw;
        }
    }

    /// <summary>
    /// Initialize default tenant for backward compatibility
    /// </summary>
    private void InitializeDefaultTenant()
    {
        var defaultTenant = new TenantContext
        {
            TenantId = "default",
            Name = "Default Tenant",
            Tier = TenantTier.Enterprise,
            IsActive = true,
            MaxAgents = 100,
            MaxMemoryEntries = 100000,
            MaxTokensPerMonth = long.MaxValue,
            DatabasePath = "./data/memory.db"
        };

        using var db = new LiteDatabase(_tenantsDbPath);
        var collection = db.GetCollection<TenantContext>("tenants");
        collection.EnsureIndex(x => x.TenantId, unique: true);

        var existing = collection.FindOne(x => x.TenantId == "default");
        if (existing == null)
        {
            collection.Insert(defaultTenant);
            _logger.LogInformation("Initialized default tenant");
        }

        _tenantCache["default"] = defaultTenant;
        _currentTenant.Value = defaultTenant;
    }

    /// <summary>
    /// Set resource limits based on tenant tier
    /// </summary>
    private static void SetTierLimits(TenantContext tenant)
    {
        switch (tenant.Tier)
        {
            case TenantTier.Free:
                tenant.MaxAgents = 5;
                tenant.MaxMemoryEntries = 1000;
                tenant.MaxTokensPerMonth = 100000;
                break;
            case TenantTier.Basic:
                tenant.MaxAgents = 20;
                tenant.MaxMemoryEntries = 10000;
                tenant.MaxTokensPerMonth = 1000000;
                break;
            case TenantTier.Premium:
                tenant.MaxAgents = 50;
                tenant.MaxMemoryEntries = 50000;
                tenant.MaxTokensPerMonth = 10000000;
                break;
            case TenantTier.Enterprise:
                tenant.MaxAgents = 200;
                tenant.MaxMemoryEntries = 200000;
                tenant.MaxTokensPerMonth = long.MaxValue;
                break;
        }
    }
}
