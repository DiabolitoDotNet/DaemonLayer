namespace InfernalHierarchy.Core.Interfaces;

public interface IAgentQuotaService
{
    void EnsureCanCreateAgent(AgentRank rank);
}