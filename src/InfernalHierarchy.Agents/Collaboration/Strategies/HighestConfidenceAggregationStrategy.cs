
namespace InfernalHierarchy.Agents.Collaboration.Strategies;

public sealed class HighestConfidenceAggregationStrategy : IAggregationStrategy
{
    public CollaborationStrategy Strategy => CollaborationStrategy.HighestConfidence;

    public CollaborationResult Aggregate(IReadOnlyList<AgentResponse> responses)
    {
        var winner = responses.OrderByDescending(r => r.Confidence).First();

        var similarResponses = responses
            .Where(r => r.Response.Trim().ToLowerInvariant() == winner.Response.Trim().ToLowerInvariant())
            .ToList();

        var agreementScore = (double)similarResponses.Count / responses.Count;

        return new CollaborationResult
        {
            Decision = winner.Response,
            Confidence = winner.Confidence,
            Responses = responses.ToList(),
            ParticipantCount = responses.Count,
            AgreementScore = agreementScore,
            Strategy = Strategy,
            WinningResponse = winner,
            AggregatedReasoning = $"Highest confidence response from {winner.AgentId}:\n{winner.Reasoning}"
        };
    }
}
