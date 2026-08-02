
namespace InfernalHierarchy.Agents.ReAct;

public interface IReActLoopRunner
{
    Task<ReActLoopResult> RunAsync(ReActLoopContext context, CancellationToken ct);
}

public sealed record ReActCheckpoint(
    string Phase,
    string Label,
    string? Detail,
    int Iteration,
    DateTime OccurredAtUtc);

public sealed record ReActLoopContext(
    string SystemContext,
    string Task,
    Persona Persona,
    ILlmClient LlmClient,
    IToolRegistry ToolRegistry,
    IActionParser ActionParser,
    IActionExecutor ActionExecutor,
    ILogger Logger,
    Action<AgentStatus> SetStatus,
    string AgentId,
    string AgentName,
    AgentRank AgentRank,
    ReActOptions ReActOptions,
    IReActPromptBuilder PromptBuilder,
    Func<ReActCheckpoint, CancellationToken, Task>? EmitCheckpoint = null,
    string? ExecutionProfile = null);

public sealed record ReActLoopResult(
    string FinalAnswer,
    string Reasoning,
    int Iterations,
    IReadOnlyList<string> ToolCalls);
