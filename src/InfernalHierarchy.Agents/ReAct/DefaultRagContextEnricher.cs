using System.Text;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultRagContextEnricher : IRagContextEnricher
{
    public async Task<string> EnrichAsync(
        string baseContext,
        string query,
        string agentId,
        AgentRank agentRank,
        IVectorMemory? vectorMemory,
        RagOptions ragOptions,
        ILogger logger,
        CancellationToken ct)
    {
        if (!ragOptions.Enabled)
        {
            return baseContext;
        }

        if (vectorMemory == null)
        {
            return baseContext;
        }

        IReadOnlyList<Fact> facts;
        try
        {
            facts = await vectorMemory.SearchSimilarVisibleFactsAsync(
                query,
                requestingAgentId: agentId,
                requestingAgentRank: agentRank,
                limit: ragOptions.MaxFacts,
                minScore: ragOptions.MinScore,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RAG retrieval failed; continuing without retrieved facts");
            return baseContext;
        }

        if (facts.Count == 0)
        {
            return baseContext;
        }

        var sb = new StringBuilder(baseContext);
        sb.AppendLine("\n\n## Retrieved Facts (RAG)");

        foreach (var fact in facts)
        {
            var content = fact.Content ?? string.Empty;
            if (ragOptions.MaxCharsPerFact > 0 && content.Length > ragOptions.MaxCharsPerFact)
            {
                content = content[..ragOptions.MaxCharsPerFact] + "…";
            }

            sb.AppendLine($"- [{fact.Category}] {content} (Source: {fact.Source}, Confidence: {fact.Confidence:P0})");
        }

        return sb.ToString();
    }
}
