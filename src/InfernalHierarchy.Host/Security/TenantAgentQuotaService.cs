using InfernalHierarchy.Agents.Registry;
using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Host.Security;

public sealed class TenantAgentQuotaService : IAgentQuotaService
{
    private readonly AgentRegistry _registry;
    private readonly ITenantIsolationService? _tenantIsolationService;
    private readonly ILogger<TenantAgentQuotaService> _logger;

    public TenantAgentQuotaService(
        AgentRegistry registry,
        ILogger<TenantAgentQuotaService> logger,
        ITenantIsolationService? tenantIsolationService = null)
    {
        _registry = registry;
        _logger = logger;
        _tenantIsolationService = tenantIsolationService;
    }

    public void EnsureCanCreateAgent(AgentRank rank)
    {
        var tenant = _tenantIsolationService?.GetCurrentTenant();
        if (tenant is null)
        {
            return;
        }

        var totalAgents = _registry.Count();
        if (totalAgents >= tenant.MaxAgents)
        {
            _logger.LogWarning(
                "🚫 Tenant {TenantId} exceeded agent quota: {Count}/{MaxAgents} while creating rank {Rank}",
                tenant.TenantId,
                totalAgents,
                tenant.MaxAgents,
                rank);

            throw new InvalidOperationException(
                $"Tenant '{tenant.TenantId}' exceeded agent quota ({tenant.MaxAgents}). No more agents can be created.");
        }
    }
}