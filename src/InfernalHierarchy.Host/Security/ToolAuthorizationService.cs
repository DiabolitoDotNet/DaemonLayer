using InfernalHierarchy.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Threading;

namespace InfernalHierarchy.Host.Security;

/// <summary>
/// Service that enforces tool authorization based on agent rank and permissions
/// </summary>
public class ToolAuthorizationService : IToolAuthorizationService
{
    private readonly ILogger<ToolAuthorizationService> _logger;
    private readonly IConfiguration _configuration;
    private ImmutableDictionary<string, ToolPermissions> _toolPermissions;

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
        var normalizedToolName = NormalizeToolName(toolName);
        if (string.IsNullOrWhiteSpace(normalizedToolName))
        {
            return AuthorizationResult.Failure("Tool name is required");
        }

        // Get tool permissions
        var permissionsSnapshot = _toolPermissions;
        if (!permissionsSnapshot.TryGetValue(normalizedToolName, out var permissions))
        {
            // Custom tools are powerful and should not be fail-open.
            // By convention, dynamically generated tools are prefixed with "custom_".
            if (normalizedToolName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
            {
                if (rank != AgentRank.Supreme)
                {
                    _logger.LogWarning(
                        "🚫 Agent {AgentName} ({Rank}) denied access to {Tool} - custom tools are Supreme-only by default",
                        agentName,
                        rank,
                        normalizedToolName);
                    return AuthorizationResult.Failure("Custom tools are Supreme-only by default. Configure ToolPermissions to delegate.");
                }

                return AuthorizationResult.Success();
            }

            // Unknown tools are denied by default to avoid policy bypasses.
            _logger.LogWarning("🚫 Tool {Tool} is not configured in ToolPermissions and is denied by default", normalizedToolName);
            return AuthorizationResult.Failure($"Tool '{normalizedToolName}' is not configured in ToolPermissions");
        }

        // Check if tool is globally disabled
        if (!permissions.Enabled)
        {
            _logger.LogWarning("🚫 Tool {Tool} is globally disabled", normalizedToolName);
            return AuthorizationResult.Failure($"Tool '{normalizedToolName}' is currently disabled");
        }

        // Check rank-based access
        if (!permissions.AllowedRanks.Contains(rank))
        {
            _logger.LogWarning("🚫 Agent {AgentName} ({Rank}) denied access to {Tool} - insufficient rank",
                agentName, rank, normalizedToolName);
            return AuthorizationResult.Failure($"Rank '{rank}' is not authorized to use tool '{normalizedToolName}'");
        }

        // Check agent-specific blacklist
        if (ContainsAgentIdentity(permissions.BlacklistedAgents, agentId, agentName))
        {
            _logger.LogWarning("🚫 Agent {AgentName} is blacklisted from {Tool}", agentName, normalizedToolName);
            return AuthorizationResult.Failure($"Agent '{agentName}' is not authorized to use tool '{normalizedToolName}'");
        }

        // Check agent-specific whitelist (if exists, must be in it)
        if (permissions.WhitelistedAgents.Count > 0)
        {
            if (!ContainsAgentIdentity(permissions.WhitelistedAgents, agentId, agentName))
            {
                _logger.LogWarning("🚫 Agent {AgentName} not in whitelist for {Tool}", agentName, normalizedToolName);
                return AuthorizationResult.Failure($"Agent '{agentName}' is not in the whitelist for tool '{normalizedToolName}'");
            }
        }

        _logger.LogDebug("✅ Agent {AgentName} ({Rank}) authorized to use {Tool}", agentName, rank, normalizedToolName);
        return AuthorizationResult.Success();
    }

    /// <summary>
    /// Get all tools available to an agent based on their rank and permissions
    /// </summary>
    public List<string> GetAuthorizedTools(string agentId, string agentName, AgentRank rank)
    {
        var authorizedTools = new List<string>();
        var permissionsSnapshot = _toolPermissions;

        foreach (var (toolName, permissions) in permissionsSnapshot)
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
        Interlocked.Exchange(ref _toolPermissions, newPermissions);
        _logger.LogInformation("✅ Tool permissions reloaded - {Count} tools configured", _toolPermissions.Count);
    }

    private ImmutableDictionary<string, ToolPermissions> LoadToolPermissions()
    {
        // Start from built-in defaults so new tools remain safely configured
        // even when the user provides a partial ToolPermissions section.
        var permissions = GetDefaultPermissions().ToBuilder();

        // Load from configuration (ToolPermissions section)
        var configSection = _configuration.GetSection("ToolPermissions");
        if (!configSection.Exists())
        {
            _logger.LogInformation("No ToolPermissions configuration found, using defaults");
            return permissions.ToImmutable();
        }

        foreach (var toolSection in configSection.GetChildren())
        {
            var toolName = NormalizeToolName(toolSection.Key);
            if (string.IsNullOrWhiteSpace(toolName))
            {
                continue;
            }

            var toolPerms = new ToolPermissions
            {
                Enabled = toolSection.GetValue<bool>("Enabled", true),
                AllowedRanks = ParseRanks(toolSection.GetValue<string>("AllowedRanks", "Supreme,Prince,Duke,Worker"), toolName),
                WhitelistedAgents = toolSection.GetSection("WhitelistedAgents").Get<List<string>>() ?? new(),
                BlacklistedAgents = toolSection.GetSection("BlacklistedAgents").Get<List<string>>() ?? new()
            };

            permissions[toolName] = toolPerms;
        }

        _logger.LogInformation("✅ Loaded permissions for {Count} tools", permissions.Count);
        return permissions.ToImmutable();
    }

    private ImmutableDictionary<string, ToolPermissions> GetDefaultPermissions()
    {
        return new Dictionary<string, ToolPermissions>(StringComparer.OrdinalIgnoreCase)
        {
            ["get_agent_status"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["create_sub_agent"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["create_custom_tool"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["publish_custom_tools_github"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme },
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
            ["send_telegram"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["send_agent_message"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["request_collaboration"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["request_skill_pack"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["create_agent_from_template"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["list_templates"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["prompt_ab_test"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["custom_tool_get_source"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["custom_tool_list"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["custom_tool_delete"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["email_send"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["brave_search"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["audio_transcribe"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["tts_speak"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["fs_read"] = new()
            {
                Enabled = false,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["fs_search"] = new()
            {
                Enabled = false,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["fs_write"] = new()
            {
                Enabled = false,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["http_request"] = new()
            {
                Enabled = false,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["graphql_request"] = new()
            {
                Enabled = false,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["sql_query_readonly"] = new()
            {
                Enabled = false,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["python_exec"] = new()
            {
                Enabled = false,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["node_exec"] = new()
            {
                Enabled = false,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            }
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeToolName(string? toolName)
    {
        return (toolName ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static bool ContainsAgentIdentity(IReadOnlyCollection<string> identities, string agentId, string agentName)
    {
        return identities.Contains(agentId, StringComparer.OrdinalIgnoreCase)
            || identities.Contains(agentName, StringComparer.OrdinalIgnoreCase);
    }

    private List<AgentRank> ParseRanks(string ranksString, string toolName)
    {
        var ranks = new HashSet<AgentRank>();
        var invalidTokens = new List<string>();
        var rankNames = ranksString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rankName in rankNames)
        {
            if (Enum.TryParse<AgentRank>(rankName, true, out var rank))
            {
                ranks.Add(rank);
            }
            else
            {
                invalidTokens.Add(rankName);
            }
        }

        if (invalidTokens.Count > 0)
        {
            _logger.LogWarning(
                "Ignoring invalid AgentRank values for tool {Tool}: {InvalidRanks}",
                toolName,
                string.Join(", ", invalidTokens));
        }

        if (ranks.Count == 0)
        {
            _logger.LogWarning(
                "No valid AgentRank configured for tool {Tool}; falling back to all ranks",
                toolName);

            ranks.UnionWith(Enum.GetValues<AgentRank>());
        }

        return ranks.ToList();
    }
}
