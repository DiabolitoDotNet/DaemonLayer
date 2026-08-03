using InfernalHierarchy.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
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
    private ExecutionProfilesOptions _executionProfiles;
    private ImmutableDictionary<string, ToolPermissions> _toolPermissions;
    private ImmutableDictionary<string, FrozenSet<string>> _profileCommandAllowlists;

    public ToolAuthorizationService(ILogger<ToolAuthorizationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _toolPermissions = LoadToolPermissions();
        _executionProfiles = LoadExecutionProfiles();
        _profileCommandAllowlists = BuildProfileCommandAllowlists(_executionProfiles);
        LogProfilePermissionDrift(_toolPermissions, _executionProfiles);
    }

    /// <summary>
    /// Check if an agent is authorized to use a specific tool
    /// </summary>
    public AuthorizationResult IsAuthorized(
        string agentId,
        string agentName,
        AgentRank rank,
        string toolName,
        string? executionProfile = null,
        IReadOnlyDictionary<string, object>? toolParameters = null)
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

        var profileDecision = EvaluateExecutionProfile(normalizedToolName, executionProfile, toolParameters);
        if (!profileDecision.IsAuthorized)
        {
            _logger.LogWarning(
                "🚫 Agent {AgentName} ({Rank}) denied access to {Tool} by execution profile {Profile}: {Reason}",
                agentName,
                rank,
                normalizedToolName,
                executionProfile ?? _executionProfiles.DefaultProfile,
                profileDecision.Reason);
            return profileDecision;
        }

        _logger.LogDebug("✅ Agent {AgentName} ({Rank}) authorized to use {Tool}", agentName, rank, normalizedToolName);
        return AuthorizationResult.Success();
    }

    /// <summary>
    /// Get all tools available to an agent based on their rank and permissions
    /// </summary>
    public List<string> GetAuthorizedTools(string agentId, string agentName, AgentRank rank, string? executionProfile = null)
    {
        var authorizedTools = new List<string>();
        var permissionsSnapshot = _toolPermissions;

        foreach (var (toolName, permissions) in permissionsSnapshot)
        {
            if (IsAuthorized(agentId, agentName, rank, toolName, executionProfile).IsAuthorized)
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
        _executionProfiles = LoadExecutionProfiles();
        _profileCommandAllowlists = BuildProfileCommandAllowlists(_executionProfiles);
        LogProfilePermissionDrift(_toolPermissions, _executionProfiles);
        _logger.LogInformation("✅ Tool permissions reloaded - {Count} tools configured", _toolPermissions.Count);
    }

    private static ImmutableDictionary<string, FrozenSet<string>> BuildProfileCommandAllowlists(ExecutionProfilesOptions profiles)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (profileName, policy) in profiles.Profiles)
        {
            var normalized = policy.CommandAllowlist
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

            builder[profileName] = normalized;
        }

        return builder.ToImmutable();
    }

    private void LogProfilePermissionDrift(
        ImmutableDictionary<string, ToolPermissions> permissions,
        ExecutionProfilesOptions profiles)
    {
        if (!profiles.Enabled || profiles.Profiles.Count == 0)
        {
            return;
        }

        var driftMessages = new List<string>();

        foreach (var (profileName, profile) in profiles.Profiles)
        {
            if (!profile.Enabled || profile.AllowedTools.Count == 0)
            {
                continue;
            }

            foreach (var tool in profile.AllowedTools)
            {
                var normalized = NormalizeToolName(tool);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (!permissions.TryGetValue(normalized, out var permission))
                {
                    driftMessages.Add($"profile={profileName} tool={normalized} reason=missing_tool_permission");
                    continue;
                }

                if (!permission.Enabled)
                {
                    driftMessages.Add($"profile={profileName} tool={normalized} reason=tool_permission_disabled");
                }
            }
        }

        if (driftMessages.Count == 0)
        {
            _logger.LogInformation("✅ Execution profile and tool permission alignment: no drift detected");
            return;
        }

        _logger.LogWarning(
            "⚠️ Execution profile/tool permission drift detected ({Count}): {Details}",
            driftMessages.Count,
            string.Join("; ", driftMessages));
    }

    private ExecutionProfilesOptions LoadExecutionProfiles()
    {
        var section = _configuration.GetSection("ExecutionProfiles");
        if (!section.Exists())
        {
            return new ExecutionProfilesOptions
            {
                Enabled = false,
                DefaultProfile = "Research",
                Profiles = GetDefaultExecutionProfiles()
            };
        }

        var configured = section.Get<ExecutionProfilesOptions>() ?? new ExecutionProfilesOptions();

        if (configured.Profiles.Count == 0)
        {
            configured.Profiles = GetDefaultExecutionProfiles();
        }

        configured.DefaultProfile = string.IsNullOrWhiteSpace(configured.DefaultProfile)
            ? "Research"
            : configured.DefaultProfile.Trim();

        return configured;
    }

    private AuthorizationResult EvaluateExecutionProfile(
        string normalizedToolName,
        string? executionProfile,
        IReadOnlyDictionary<string, object>? toolParameters)
    {
        if (!_executionProfiles.Enabled)
        {
            return AuthorizationResult.Success();
        }

        var profileName = string.IsNullOrWhiteSpace(executionProfile)
            ? _executionProfiles.DefaultProfile
            : executionProfile.Trim();

        if (!_executionProfiles.Profiles.TryGetValue(profileName, out var policy))
        {
            return AuthorizationResult.Failure($"Execution profile '{profileName}' is not configured");
        }

        if (!policy.Enabled)
        {
            return AuthorizationResult.Failure($"Execution profile '{profileName}' is disabled");
        }

        if (policy.DeniedTools.Contains(normalizedToolName, StringComparer.OrdinalIgnoreCase))
        {
            return AuthorizationResult.Failure(
                $"Execution profile '{profileName}' denies tool '{normalizedToolName}'");
        }

        if (policy.AllowedTools.Count > 0
            && !policy.AllowedTools.Contains(normalizedToolName, StringComparer.OrdinalIgnoreCase))
        {
            return AuthorizationResult.Failure(
                $"Tool '{normalizedToolName}' is not allowed by execution profile '{profileName}'");
        }

        var scopeDecision = EvaluateExecutionScopes(normalizedToolName, profileName, policy, toolParameters);
        if (!scopeDecision.IsAuthorized)
        {
            return scopeDecision;
        }

        return AuthorizationResult.Success();
    }

    private AuthorizationResult EvaluateExecutionScopes(
        string normalizedToolName,
        string profileName,
        ExecutionProfilePolicy policy,
        IReadOnlyDictionary<string, object>? toolParameters)
    {
        var parameters = toolParameters ?? new Dictionary<string, object>();

        if (policy.AllowedFileScopes.Count > 0)
        {
            foreach (var fileTarget in ExtractFileTargets(normalizedToolName, parameters))
            {
                if (!MatchesFileScope(fileTarget, policy.AllowedFileScopes))
                {
                    return AuthorizationResult.Failure(
                        $"Path '{fileTarget}' is outside allowed file scopes for execution profile '{profileName}'");
                }
            }
        }

        if (policy.AllowedNetworkScopes.Count > 0)
        {
            foreach (var networkTarget in ExtractNetworkTargets(normalizedToolName, parameters))
            {
                if (!MatchesNetworkScope(networkTarget, policy.AllowedNetworkScopes))
                {
                    return AuthorizationResult.Failure(
                        $"Network target '{networkTarget}' is outside allowed network scopes for execution profile '{profileName}'");
                }
            }
        }

        if (_profileCommandAllowlists.TryGetValue(profileName, out var commandAllowlist)
            && commandAllowlist.Count > 0)
        {
            foreach (var commandTarget in ExtractCommandTargets(normalizedToolName, parameters))
            {
                if (!commandAllowlist.Contains(commandTarget))
                {
                    return AuthorizationResult.Failure(
                        $"Command '{commandTarget}' is not allowed by execution profile '{profileName}'");
                }
            }
        }

        return AuthorizationResult.Success();
    }

    private static IEnumerable<string> ExtractFileTargets(string toolName, IReadOnlyDictionary<string, object> toolParameters)
    {
        if (toolName.Equals("fs_read", StringComparison.OrdinalIgnoreCase)
            || toolName.Equals("fs_write", StringComparison.OrdinalIgnoreCase))
        {
            var path = TryGetString(toolParameters, "path");
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path.Trim();
            }
        }

        if (toolName.Equals("python_exec", StringComparison.OrdinalIgnoreCase)
            || toolName.Equals("node_exec", StringComparison.OrdinalIgnoreCase))
        {
            var workingDir = TryGetString(toolParameters, "working_dir");
            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                yield return workingDir.Trim();
            }
        }
    }

    private static IEnumerable<string> ExtractNetworkTargets(string toolName, IReadOnlyDictionary<string, object> toolParameters)
    {
        if (toolName.Equals("http_request", StringComparison.OrdinalIgnoreCase))
        {
            var url = TryGetString(toolParameters, "url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                yield return url.Trim();
            }
        }

        if (toolName.Equals("graphql_request", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = TryGetString(toolParameters, "endpoint") ?? TryGetString(toolParameters, "url");
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                yield return endpoint.Trim();
            }
        }
    }

    private static IEnumerable<string> ExtractCommandTargets(string toolName, IReadOnlyDictionary<string, object> toolParameters)
    {
        if (toolName.Equals("python_exec", StringComparison.OrdinalIgnoreCase))
        {
            yield return "python_exec";
            yield return "python";
        }

        if (toolName.Equals("node_exec", StringComparison.OrdinalIgnoreCase))
        {
            yield return "node_exec";
            yield return "node";
        }

        if (toolName.Equals("workflow_step", StringComparison.OrdinalIgnoreCase))
        {
            yield return "workflow_step";
        }

        if (toolName.Equals("deploy_adapter", StringComparison.OrdinalIgnoreCase))
        {
            yield return "deploy_adapter";
        }

        var explicitCommand = TryGetString(toolParameters, "command")
            ?? TryGetString(toolParameters, "executable")
            ?? TryGetString(toolParameters, "shell_command");

        if (!string.IsNullOrWhiteSpace(explicitCommand))
        {
            yield return explicitCommand.Trim();
        }
    }

    private static bool MatchesFileScope(string path, IReadOnlyCollection<string> allowedScopes)
    {
        if (allowedScopes.Count == 0)
        {
            return true;
        }

        var normalizedPath = NormalizePath(path);

        foreach (var scope in allowedScopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                continue;
            }

            var normalizedScope = NormalizePath(scope);

            if (normalizedScope == "*")
            {
                return true;
            }

            if (normalizedScope.EndsWith("/**", StringComparison.Ordinal))
            {
                var prefix = normalizedScope[..^3].TrimEnd('/');
                if (normalizedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (normalizedScope.EndsWith("/*", StringComparison.Ordinal))
            {
                var prefix = normalizedScope[..^2].TrimEnd('/');
                if (normalizedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (normalizedPath.Equals(normalizedScope, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedScope + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesNetworkScope(string target, IReadOnlyCollection<string> allowedScopes)
    {
        if (allowedScopes.Count == 0)
        {
            return true;
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return false;
        }

        foreach (var scope in allowedScopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                continue;
            }

            var trimmed = scope.Trim();
            if (trimmed == "*")
            {
                return true;
            }

            if (trimmed.StartsWith(".", StringComparison.Ordinal)
                && uri.Host.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var allowedUri))
            {
                if (!string.Equals(uri.Scheme, allowedUri.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(uri.Host, allowedUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!allowedUri.IsDefaultPort && uri.Port != allowedUri.Port)
                {
                    continue;
                }

                var prefix = allowedUri.AbsolutePath.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(prefix) || prefix == "/")
                {
                    return true;
                }

                if (uri.AbsolutePath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                    || uri.AbsolutePath.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (string.Equals(uri.Host, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePath(string value)
    {
        return value.Trim().Replace('\\', '/').TrimEnd('/');
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is System.Text.Json.JsonElement element)
        {
            return element.ValueKind == System.Text.Json.JsonValueKind.String ? element.GetString() : element.ToString();
        }

        return value.ToString();
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
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["fs_search"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["fs_write"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["http_request"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["graphql_request"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["sql_query_readonly"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["python_exec"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["node_exec"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["repo_analyze"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke, AgentRank.Worker },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["workflow_step"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince, AgentRank.Duke },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            },
            ["deploy_adapter"] = new()
            {
                Enabled = true,
                AllowedRanks = new() { AgentRank.Supreme, AgentRank.Prince },
                WhitelistedAgents = new(),
                BlacklistedAgents = new()
            }
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, ExecutionProfilePolicy> GetDefaultExecutionProfiles()
    {
        return new Dictionary<string, ExecutionProfilePolicy>(StringComparer.OrdinalIgnoreCase)
        {
            ["Research"] = new ExecutionProfilePolicy
            {
                Enabled = true,
                AllowedTools =
                [
                    "web_search", "read_memory", "write_memory", "request_collaboration",
                    "get_agent_status", "request_skill_pack", "create_sub_agent", "send_agent_message",
                    "create_agent_from_template", "list_templates", "email_send", "send_telegram",
                    "vision_describe", "audio_transcribe", "tts_speak", "repo_analyze"
                ],
                DeniedTools = ["fs_write", "python_exec", "node_exec"]
            },
            ["Build"] = new ExecutionProfilePolicy
            {
                Enabled = true,
                AllowedTools =
                [
                    "web_search", "read_memory", "write_memory", "request_collaboration",
                    "get_agent_status", "request_skill_pack", "fs_read", "fs_search", "fs_write",
                    "http_request", "graphql_request", "sql_query_readonly", "python_exec", "node_exec",
                    "create_custom_tool", "custom_tool_list", "custom_tool_get_source", "custom_tool_delete",
                    "vision_describe", "audio_transcribe", "repo_analyze", "workflow_step"
                ]
            },
            ["Deploy"] = new ExecutionProfilePolicy
            {
                Enabled = true,
                AllowedTools =
                [
                    "web_search", "read_memory", "write_memory", "request_collaboration",
                    "get_agent_status", "request_skill_pack", "fs_read", "fs_search", "http_request",
                    "python_exec", "node_exec", "create_custom_tool", "custom_tool_list", "custom_tool_get_source",
                    "repo_analyze", "workflow_step", "deploy_adapter"
                ],
                DeniedTools = ["fs_write", "custom_tool_delete"]
            }
        };
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

    private static bool ContainsIgnoreCase(IReadOnlyCollection<string> values, string candidate)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (string.Equals(value.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
