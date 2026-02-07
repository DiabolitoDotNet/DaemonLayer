
namespace InfernalHierarchy.Agents.Collaboration.Strategies;

public sealed class VotingAggregationStrategy : IAggregationStrategy
{
    public CollaborationStrategy Strategy => CollaborationStrategy.Voting;

    public CollaborationResult Aggregate(IReadOnlyList<AgentResponse> responses)
    {
        var grouped = responses
            .GroupBy(r => r.Response.Trim().ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .ToList();

        var winningGroup = grouped.FirstOrDefault();
        if (winningGroup == null)
        {
            return new CollaborationResult
            {
                Decision = "NO_CONSENSUS",
                Confidence = 0.0,
                Responses = responses.ToList(),
                ParticipantCount = responses.Count,
                AgreementScore = 0.0,
                Strategy = Strategy
            };
        }

        var winningResponse = winningGroup.OrderByDescending(r => r.Confidence).First();
        var agreementScore = (double)winningGroup.Count() / responses.Count;

        return new CollaborationResult
        {
            Decision = winningResponse.Response,
            Confidence = agreementScore,
            Responses = responses.ToList(),
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            Strategy = Strategy,
            WinningResponse = winningResponse,
            AggregatedReasoning = string.Join("\n\n", winningGroup.Select(r => $"[{r.AgentId}]: {r.Reasoning}"))
        };
    }
}
