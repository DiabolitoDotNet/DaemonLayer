using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.Agents.ReAct;

public interface IActionExecutor
{
    Task<ActionExecutionResult> ExecuteAsync(ActionExecutionContext context);
}

public sealed record ActionExecutionContext(
    IToolRegistry ToolRegistry,
    string ToolName,
    string ActionInputText,
    Dictionary<string, object>? ActionInputObject,
    string AgentId,
    string AgentRank,
    IReadOnlyCollection<string> AvailableTools,
    CancellationToken CancellationToken);

public sealed record ActionExecutionResult(
    bool ToolFound,
    bool Success,
    string Observation,
    string? ToolCall,
    string? Error);
