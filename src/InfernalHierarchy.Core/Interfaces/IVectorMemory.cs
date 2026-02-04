using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Abstraction for semantic/vector memory operations (e.g. Qdrant + embeddings).
/// </summary>
public interface IVectorMemory
{
    Task InitializeCollectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Persist a fact in shared memory and (when enabled) index it into the vector store.
    /// </summary>
    Task IndexFactAsync(Fact fact, CancellationToken ct = default);

    /// <summary>
    /// Retrieve semantically similar facts visible to the requesting agent.
    /// Implementations should fall back to keyword search when vector search is unavailable.
    /// </summary>
    Task<IReadOnlyList<Fact>> SearchSimilarVisibleFactsAsync(
        string query,
        string requestingAgentId,
        AgentRank requestingAgentRank,
        int limit = 8,
        double minScore = 0.70,
        CancellationToken ct = default);
}
