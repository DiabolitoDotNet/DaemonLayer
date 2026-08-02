
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Authorizes tool usage for an agent based on rank and configured permissions.
/// Defined in Core so execution pipelines (Tools) can enforce security without depending on Host.
/// </summary>
public interface IToolAuthorizationService
{
    /// <summary>
    /// Evaluates whether an agent is allowed to use a tool.
    /// </summary>
    /// <param name="agentId">Agent id.</param>
    /// <param name="agentName">Agent display name.</param>
    /// <param name="rank">Agent rank.</param>
    /// <param name="toolName">Tool name (<see cref="ITool.Name"/>).</param>
    /// <param name="executionProfile">Optional execution profile used for policy scoping.</param>
    /// <param name="toolParameters">Optional tool parameter bag used for profile scope checks.</param>
    AuthorizationResult IsAuthorized(
        string agentId,
        string agentName,
        AgentRank rank,
        string toolName,
        string? executionProfile = null,
        IReadOnlyDictionary<string, object>? toolParameters = null);

    /// <summary>
    /// Returns the set of tools currently authorized for the agent.
    /// </summary>
    List<string> GetAuthorizedTools(string agentId, string agentName, AgentRank rank, string? executionProfile = null);

    /// <summary>
    /// Reloads the permissions configuration.
    /// Implementations should be safe to call multiple times.
    /// </summary>
    void ReloadPermissions();
}

/// <summary>
/// Configuration describing tool permission rules for a tool.
/// </summary>
public class ToolPermissions
{
    /// <summary>
    /// When false, the tool is disabled regardless of other rules.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Ranks allowed to execute the tool.
    /// An empty list is interpreted by implementations as "no rank-based allowance" (deny unless whitelisted).
    /// </summary>
    public List<AgentRank> AllowedRanks { get; set; } = new();

    /// <summary>
    /// Explicitly allowed agent names/ids (implementation-defined matching).
    /// </summary>
    public List<string> WhitelistedAgents { get; set; } = new();

    /// <summary>
    /// Explicitly denied agent names/ids.
    /// </summary>
    public List<string> BlacklistedAgents { get; set; } = new();
}

/// <summary>
/// Result of a tool authorization check.
/// </summary>
public class AuthorizationResult
{
    /// <summary>
    /// True when authorized.
    /// </summary>
    public bool IsAuthorized { get; set; }

    /// <summary>
    /// Optional reason when not authorized.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static AuthorizationResult Success() => new() { IsAuthorized = true };

    /// <summary>
    /// Creates a failed authorization result.
    /// </summary>
    public static AuthorizationResult Failure(string reason) => new() { IsAuthorized = false, Reason = reason };
}
