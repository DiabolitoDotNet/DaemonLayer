
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Authorizes tool usage for an agent based on rank and configured permissions.
/// Defined in Core so execution pipelines (Tools) can enforce security without depending on Host.
/// </summary>
public interface IToolAuthorizationService
{
    AuthorizationResult IsAuthorized(string agentId, string agentName, AgentRank rank, string toolName);
    List<string> GetAuthorizedTools(string agentId, string agentName, AgentRank rank);
    void ReloadPermissions();
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
