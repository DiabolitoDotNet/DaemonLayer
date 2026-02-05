namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Cross-cutting execution pipeline for tools (authorization, validation, retry, metrics, auditing, learning, etc.).
/// </summary>
public interface IToolExecutionPipeline
{
    Task<ToolResult> ExecuteAsync(ToolExecutionContext context);
}

public sealed record ToolExecutionContext(
    string ToolName,
    ITool Tool,
    Dictionary<string, object> Parameters,
    string? AgentId,
    string? AgentRank,
    CancellationToken CancellationToken);
