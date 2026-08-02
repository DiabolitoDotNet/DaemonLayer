namespace InfernalHierarchy.Core.Interfaces;

public static class FailedOperationReplayConstants
{
    public const string ReplayAgentId = "system-deadletter-replay";
}

public sealed class ToolReplayPayload
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
    public string? AgentId { get; set; }
    public string? AgentRank { get; set; }
    public string? AgentName { get; set; }
}
