
namespace InfernalHierarchy.Agents.Collaboration.Strategies;

public sealed class WeightedVotingAggregationStrategy : IAggregationStrategy
{
    public CollaborationStrategy Strategy => CollaborationStrategy.WeightedVoting;

    public CollaborationResult Aggregate(IReadOnlyList<AgentResponse> responses)
    {
        var grouped = responses
            .GroupBy(r => r.Response.Trim().ToLowerInvariant())
            .Select(g => new
            {
                Response = g.First().Response,
                TotalWeight = g.Sum(r => r.Weight * r.Confidence),
                Responses = g.ToList(),
                AverageConfidence = g.Average(r => r.Confidence)
            })
            .OrderByDescending(g => g.TotalWeight)
            .ToList();

        var winner = grouped.FirstOrDefault();
        if (winner == null)
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

        var totalWeight = responses.Sum(r => r.Weight * r.Confidence);
        var agreementScore = totalWeight <= 0 ? 0.0 : (winner.TotalWeight / totalWeight);

        return new CollaborationResult
        {
            Decision = winner.Response,
            Confidence = Math.Min(winner.AverageConfidence * agreementScore, 1.0),
            Responses = responses.ToList(),
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            Strategy = Strategy,
            WinningResponse = winner.Responses.OrderByDescending(r => r.Confidence).First(),
            AggregatedReasoning = string.Join(
                "\n\n",
                winner.Responses.Select(r => $"[{r.AgentId} (weight: {r.Weight:F2})]: {r.Reasoning}"))
        };
    }
}
