
namespace InfernalHierarchy.Agents.Collaboration.Strategies;

public sealed class HierarchicalAggregationStrategy : IAggregationStrategy
{
    public CollaborationStrategy Strategy => CollaborationStrategy.Hierarchical;

    public CollaborationResult Aggregate(IReadOnlyList<AgentResponse> responses)
    {
        var winner = responses
            .OrderByDescending(r => GetRankPriority(r.AgentRank))
            .ThenByDescending(r => r.Confidence)
            .First();

        var sameRankResponses = responses
            .Where(r => r.AgentRank == winner.AgentRank)
            .ToList();

        var sameRankAgreement = sameRankResponses
            .Count(r => r.Response.Trim().ToLowerInvariant() == winner.Response.Trim().ToLowerInvariant());

        var agreementScore = sameRankResponses.Count == 0
            ? 0.0
            : (double)sameRankAgreement / sameRankResponses.Count;

        return new CollaborationResult
        {
            Decision = winner.Response,
            Confidence = winner.Confidence * agreementScore,
            Responses = responses.ToList(),
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            Strategy = Strategy,
            WinningResponse = winner,
            AggregatedReasoning =
                $"Hierarchical decision from {winner.AgentRank} agent {winner.AgentId} " +
                $"(agreement among {winner.AgentRank} agents: {agreementScore:P0}):\n{winner.Reasoning}"
        };
    }

    private static int GetRankPriority(AgentRank rank)
        => rank switch
        {
            AgentRank.Supreme => 4,
            AgentRank.Prince => 3,
            AgentRank.Duke => 2,
            AgentRank.Worker => 1,
            _ => 0
        };
}
