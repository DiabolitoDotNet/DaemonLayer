
namespace InfernalHierarchy.Agents.ReAct;

public interface IReActLoopRunner
{
    Task<ReActLoopResult> RunAsync(ReActLoopContext context, CancellationToken ct);
}

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
    IReActPromptBuilder PromptBuilder);

public sealed record ReActLoopResult(
    string FinalAnswer,
    string Reasoning,
    int Iterations,
    IReadOnlyList<string> ToolCalls);
