namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Represents a tenant (organization/user) in the multi-tenant system
/// </summary>
public class TenantContext
{
    /// <summary>
    /// Gets or sets the unique tenant identifier
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant tier (Free, Basic, Premium, Enterprise)
    /// </summary>
    public TenantTier Tier { get; set; } = TenantTier.Free;

    /// <summary>
    /// Gets or sets when the tenant was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets whether the tenant is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of agents allowed
    /// </summary>
    public int MaxAgents { get; set; } = 10;

    /// <summary>
    /// Gets or sets the maximum memory entries allowed
    /// </summary>
    public int MaxMemoryEntries { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the maximum LLM tokens per month
    /// </summary>
    public long MaxTokensPerMonth { get; set; } = 1000000;

    /// <summary>
    /// Gets or sets custom metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets allowed Telegram user IDs for this tenant
    /// </summary>
    public List<long> AllowedUserIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the dedicated database path for this tenant
    /// </summary>
    public string? DatabasePath { get; set; }
}

/// <summary>
/// Tenant subscription tier
/// </summary>
public enum TenantTier
{
    /// <summary>
    /// Free tier with limited resources
    /// </summary>
    Free = 0,

    /// <summary>
    /// Basic paid tier
    /// </summary>
    Basic = 1,

    /// <summary>
    /// Premium tier with advanced features
    /// </summary>
    Premium = 2,

    /// <summary>
    /// Enterprise tier with maximum resources
    /// </summary>
    Enterprise = 3
}
