using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core;

public static class MemoryVisibilityRules
{
    public static bool CanView(MemoryEntry entry, string requestingAgentId, AgentRank requestingAgentRank)
    {
        if (entry.CreatedBy == requestingAgentId)
        {
            return true;
        }

        return entry.Visibility switch
        {
            MemoryVisibility.Public => true,
            MemoryVisibility.Private => false,
            MemoryVisibility.Shared => entry.SharedWithAgents.Contains(requestingAgentId),
            MemoryVisibility.RankBased => entry.MinimumRankToView.HasValue && requestingAgentRank <= entry.MinimumRankToView.Value,
            _ => false
        };
    }
}
