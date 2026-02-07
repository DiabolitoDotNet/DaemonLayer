
namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultAgentEventAppender : IAgentEventAppender
{
    public void TryAppendTaskEvent(
        IAgentEventSink? eventSink,
        string agentId,
        AgentRank agentRank,
        AgentMessage task,
        EventType type,
        string description,
        Dictionary<string, object>? extraMetadata = null)
    {
        if (eventSink == null)
        {
            return;
        }

        var metadata = new Dictionary<string, object>
        {
            ["task_id"] = task.Id,
            ["from_agent_id"] = task.FromAgentId,
            ["message_type"] = task.Type.ToString(),
            ["agent_rank"] = agentRank.ToString()
        };

        if (extraMetadata != null)
        {
            foreach (var kvp in extraMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        try
        {
            eventSink.AppendEvent(new AgentEvent
            {
                AgentId = agentId,
                Type = type,
                Description = description,
                Metadata = metadata
            });
        }
        catch
        {
            // best-effort
        }
    }

    public void TryAppendDecisionEvent(
        IAgentEventSink? eventSink,
        string agentId,
        AgentMessage task,
        int iterations,
        string reasoning,
        string answer)
    {
        if (eventSink == null)
        {
            return;
        }

        try
        {
            eventSink.AppendEvent(new AgentEvent
            {
                AgentId = agentId,
                Type = EventType.DecisionMade,
                Description = "Decision recorded",
                Metadata = new Dictionary<string, object>
                {
                    ["task_id"] = task.Id,
                    ["iterations"] = iterations,
                    ["reasoning"] = reasoning,
                    ["answer"] = answer
                }
            });
        }
        catch
        {
            // best-effort
        }
    }
}
