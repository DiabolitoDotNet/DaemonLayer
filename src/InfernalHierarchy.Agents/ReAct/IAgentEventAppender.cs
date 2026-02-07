
namespace InfernalHierarchy.Agents.ReAct;

public interface IAgentEventAppender
{
    void TryAppendTaskEvent(
        IAgentEventSink? eventSink,
        string agentId,
        AgentRank agentRank,
        AgentMessage task,
        EventType type,
        string description,
        Dictionary<string, object>? extraMetadata = null);

    void TryAppendDecisionEvent(
        IAgentEventSink? eventSink,
        string agentId,
        AgentMessage task,
        int iterations,
        string reasoning,
        string answer);
}
