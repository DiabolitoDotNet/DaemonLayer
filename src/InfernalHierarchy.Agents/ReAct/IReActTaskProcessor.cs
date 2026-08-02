
namespace InfernalHierarchy.Agents.ReAct;

public interface IReActTaskProcessor
{
    Task<AgentMessage> ProcessAsync(ReActTaskProcessorContext context, AgentMessage task, CancellationToken ct);
}

public sealed record ReActTaskProcessorContext(
    string AgentId,
    string AgentName,
    AgentRank AgentRank,
    Persona Persona,
    ILlmClient LlmClient,
    IToolRegistry ToolRegistry,
    ISharedMemory SharedMemory,
    IActionParser ActionParser,
    IActionExecutor ActionExecutor,
    IReportGenerator ReportGenerator,
    IReActPromptBuilder PromptBuilder,
    IReActLoopRunner LoopRunner,
    ReActOptions ReActOptions,
    RagOptions RagOptions,
    IVectorMemory? VectorMemory,
    IAgentCollaborationService? CollaborationService,
    IAgentSkillRuntimeStore? RuntimeSkillStore,
    IAgentEventSink? EventSink,
    Action<AgentStatus> SetStatus,
    Func<AgentMessage, CancellationToken, Task<string>> BuildBaseContextAsync,
    ILogger Logger);
