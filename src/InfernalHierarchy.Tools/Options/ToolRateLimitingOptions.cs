namespace InfernalHierarchy.Tools.Options;

public sealed class ToolRateLimitingOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Applied when no rank-specific rule exists.
    /// </summary>
    public FixedWindowRateLimitRule DefaultRule { get; set; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60
    };

    /// <summary>
    /// Rank-specific defaults (e.g. Worker/Duke/Prince/Supreme).
    /// </summary>
    public Dictionary<string, FixedWindowRateLimitRule> RankDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Worker"] = new FixedWindowRateLimitRule { PermitLimit = 30, WindowSeconds = 60 },
        ["Duke"] = new FixedWindowRateLimitRule { PermitLimit = 60, WindowSeconds = 60 },
        ["Prince"] = new FixedWindowRateLimitRule { PermitLimit = 120, WindowSeconds = 60 },
        ["Supreme"] = new FixedWindowRateLimitRule { PermitLimit = 300, WindowSeconds = 60 }
    };

    /// <summary>
    /// Per-tool overrides keyed by tool name.
    /// </summary>
    public Dictionary<string, ToolRateLimitOverride> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Remove idle rate limit entries after this many seconds without access.
    /// </summary>
    public int IdleEntryExpirationSeconds { get; set; } = 600;

    /// <summary>
    /// How many Check() calls between pruning passes.
    /// </summary>
    public int PruneEveryNChecks { get; set; } = 500;
}

public sealed class ToolRateLimitOverride
{
    /// <summary>
    /// If false, disables rate limiting for this tool.
    /// If null, falls back to global Enabled.
    /// </summary>
    public bool? Enabled { get; set; }

    public FixedWindowRateLimitRule? DefaultRule { get; set; }

    public Dictionary<string, FixedWindowRateLimitRule>? RankDefaults { get; set; }
}

public sealed class FixedWindowRateLimitRule
{
    /// <summary>
    /// Maximum number of tool executions allowed per window.
    /// </summary>
    public int PermitLimit { get; set; }

    /// <summary>
    /// Window size in seconds.
    /// </summary>
    public int WindowSeconds { get; set; }
}
