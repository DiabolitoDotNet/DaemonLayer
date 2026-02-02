using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host;

/// <summary>
/// Service that enforces tool authorization based on agent rank and permissions
/// </summary>
public class ToolAuthorizationService
{
    private readonly ILogger<ToolAuthorizationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, ToolPermissions> _toolPermissions;

    public ToolAuthorizationService(ILogger<ToolAuthorizationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _toolPermissions = LoadToolPermissions();
    }

    /// <summary>
    /// Check if an agent is authorized to use a specific tool
    /// </summary>
    public AuthorizationResult IsAuthorized(string agentId, string agentName, AgentRank rank, string toolName)
    {
        // Get tool permissions
        if (!_toolPermissions.TryGetValue(toolName, out var permissions))
        {
            // Tool not in permissions list - allow by default (fail-open for extensibility)
            _logger.LogDebug("Tool {Tool} not in permissions list, allowing access", toolName);
            return AuthorizationResult.Success();
        }

        // Check if tool is globally disabled
        if (!permissions.Enabled)
        {
            _logger.LogWarning("🚫 Tool {Tool} is globally disabled", toolName);
            return AuthorizationResult.Failure($"Tool '{toolName}' is currently disabled");
        }

        // Check rank-based access
        if (!permissions.AllowedRanks.Contains(rank))
        {
            _logger.LogWarning("🚫 Agent {AgentName} ({Rank}) denied access to {Tool} - insufficient rank",
                agentName, rank, toolName);
            return AuthorizationResult.Failure($"Rank '{rank}' is not authorized to use tool '{toolName}'");
        }

        // Check agent-specific blacklist
        if (permissions.BlacklistedAgents.Contains(agentId) || permissions.BlacklistedAgents.Contains(agentName))
        {
            _logger.LogWarning("🚫 Agent {AgentName} is blacklisted from {Tool}", agentName, toolName);
            return AuthorizationResult.Failure($"Agent '{agentName}' is not authorized to use tool '{toolName}'");
        }

        // Check agent-specific whitelist (if exists, must be in it)
        if (permissions.WhitelistedAgents.Count > 0)
        {
            if (!permissions.WhitelistedAgents.Contains(agentId) && !permissions.WhitelistedAgents.Contains(agentName))
            {
                _logger.LogWarning("🚫 Agent {AgentName} not in whitelist for {Tool}", agentName, toolName);
                return AuthorizationResult.Failure($"Agent '{agentName}' is not in the whitelist for tool '{toolName}'");
            }
        }

        _logger.LogDebug("✅ Agent {AgentName} ({Rank}) authorized to use {Tool}", agentName, rank, toolName);
        return AuthorizationResult.Success();
    }

    /// <summary>
    /// Get all tools available to an agent based on their rank and permissions
    /// </summary>
    public List<string> GetAuthorizedTools(string agentId, string agentName, AgentRank rank)
    {
        var authorizedTools = new List<string>();

        foreach (var (toolName, permissions) in _toolPermissions)
        {
            if (IsAuthorized(agentId, agentName, rank, toolName).IsAuthorized)
            {
                authorizedTools.Add(toolName);
            }
        }

        return authorizedTools;
    }

    /// <summary>
    /// Reload tool permissions from configuration
    /// </summary>
    public void ReloadPermissions()
    {
        _logger.LogInformation("🔄 Reloading tool permissions...");
        var newPermissions = LoadToolPermissions();

        _toolPermissions.Clear();
        foreach (var (key, value) in newPermissions)
        {
            _toolPermissions[key] = value;
        }

        _logger.LogInformation("✅ Tool permissions reloaded - {Count} tools configured", _toolPermissions.Count);
    }

    private Dictionary<string, ToolPermissions> LoadToolPermissions()
    {
        var permissions = new Dictionary<string, ToolPermissions>();

        // Load from configuration (ToolPermissions section)
        var configSection = _configuration.GetSection("ToolPermissions");
        if (!configSection.Exists())
        {
            _logger.LogInformation("No ToolPermissions configuration found, using defaults");
            return GetDefaultPermissions();
        }

        foreach (var toolSection in configSection.GetChildren())
        {
            var toolName = toolSection.Key;
            var toolPerms = new ToolPermissions
            {
                Enabled = toolSection.GetValue<bool>("Enabled", true),
                AllowedRanks = ParseRanks(toolSection.GetValue<string>("AllowedRanks", "Supreme,Prince,Duke,Worker")),
                WhitelistedAgents = toolSection.GetSection("WhitelistedAgents").Get<List<string>>() ?? new(),
                BlacklistedAgents = toolSection.GetSection("BlacklistedAgents").Get<List<string>>() ?? new()
            };

            permissions[toolName] = toolPerms;
        }

        _logger.LogInformation("✅ Loaded permissions for {Count} tools", permissions.Count);
        return permissions;
    }

    private Dictionary<string, ToolPermissions> GetDefaultPermissions()
    {
        return new Dictionary<string, ToolPermissions>
        {
            ["create_sub_agent"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["web_search"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["write_memory"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["read_memory"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["telegram_send"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            }
        };
    }

    private List<AgentRank> ParseRanks(string ranksString)
    {
        var ranks = new List<AgentRank>();
        var rankNames = ranksString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rankName in rankNames)
        {
            if (Enum.TryParse<AgentRank>(rankName, true, out var rank))
            {
                ranks.Add(rank);
            }
        }

        return ranks;
    }
}

public class ToolPermissions
{
    public bool Enabled { get; set; } = true;
    public List<AgentRank> AllowedRanks { get; set; } = new();
    public List<string> WhitelistedAgents { get; set; } = new();
    public List<string> BlacklistedAgents { get; set; } = new();
}

public class AuthorizationResult
{
    public bool IsAuthorized { get; set; }
    public string? Reason { get; set; }

    public static AuthorizationResult Success() => new() { IsAuthorized = true };
    public static AuthorizationResult Failure(string reason) => new() { IsAuthorized = false, Reason = reason };
}
