namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Stores temporary runtime skill grants for active agents.
/// </summary>
public interface IAgentSkillRuntimeStore
{
    void ApplyGrant(string agentId, AgentSkillGrant grant);

    AgentSkillRuntimeOverlay GetOverlay(string agentId, DateTime utcNow);

    int PruneExpired(DateTime utcNow);
}
