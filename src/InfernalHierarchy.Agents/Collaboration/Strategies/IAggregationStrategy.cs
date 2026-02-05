using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Agents.Collaboration.Strategies;

public interface IAggregationStrategy
{
    CollaborationStrategy Strategy { get; }

    CollaborationResult Aggregate(IReadOnlyList<AgentResponse> responses);
}
