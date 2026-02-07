
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Minimal abstraction for appending agent events.
/// Keeps infrastructure concerns (disk IO) out of domain and enables unit testing.
/// </summary>
public interface IAgentEventSink
{
    void AppendEvent(AgentEvent evt);
}
