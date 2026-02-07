
namespace InfernalHierarchy.Agents.Collaboration.Strategies;

public sealed class ConsensusAggregationStrategy : IAggregationStrategy
{
    public CollaborationStrategy Strategy => CollaborationStrategy.Consensus;

    public CollaborationResult Aggregate(IReadOnlyList<AgentResponse> responses)
    {
        var uniqueResponses = responses
            .Select(r => r.Response.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (uniqueResponses.Count == 1)
        {
            var averageConfidence = responses.Average(r => r.Confidence);
            var firstResponse = responses.First();

            return new CollaborationResult
            {
                Decision = firstResponse.Response,
                Confidence = averageConfidence,
                Responses = responses.ToList(),
                ParticipantCount = responses.Count,
                AgreementScore = 1.0,
                Strategy = Strategy,
                WinningResponse = responses.OrderByDescending(r => r.Confidence).First(),
                AggregatedReasoning = string.Join("\n\n", responses.Select(r => $"[{r.AgentId}]: {r.Reasoning}"))
            };
        }

        var grouped = responses
            .GroupBy(r => r.Response.Trim().ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .ToList();

        var conflictSummary = string.Join("; ", grouped.Select(g => $"{g.Count()} voted '{g.First().Response}'"));

        return new CollaborationResult
        {
            Decision = $"NO_CONSENSUS: {conflictSummary}",
            Confidence = 0.0,
            Responses = responses.ToList(),
            ParticipantCount = responses.Count,
            AgreementScore = (double)grouped.First().Count() / responses.Count,
            Strategy = Strategy,
            AggregatedReasoning = "Agents could not reach unanimous agreement. " + conflictSummary
        };
    }
}
