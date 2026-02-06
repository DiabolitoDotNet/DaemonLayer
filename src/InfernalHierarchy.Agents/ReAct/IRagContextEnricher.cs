using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Agents.ReAct;

public interface IRagContextEnricher
{
    Task<string> EnrichAsync(
        string baseContext,
        string query,
        string agentId,
        AgentRank agentRank,
        IVectorMemory? vectorMemory,
        RagOptions ragOptions,
        ILogger logger,
        CancellationToken ct);
}
