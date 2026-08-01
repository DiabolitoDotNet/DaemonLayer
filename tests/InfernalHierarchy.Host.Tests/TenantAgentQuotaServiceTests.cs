using FluentAssertions;
using InfernalHierarchy.Agents.Registry;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class TenantAgentQuotaServiceTests
{
    [Fact]
    public void EnsureCanCreateAgent_WhenTenantQuotaReached_Throws()
    {
        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);
        registry.Register(MockAgent("a1"));
        registry.Register(MockAgent("a2"));

        var tenantIsolation = new Mock<ITenantIsolationService>();
        tenantIsolation
            .Setup(t => t.GetCurrentTenant())
            .Returns(new TenantContext
            {
                TenantId = "t1",
                Name = "Tenant 1",
                MaxAgents = 2,
                IsActive = true
            });

        var sut = new TenantAgentQuotaService(registry, NullLogger<TenantAgentQuotaService>.Instance, tenantIsolation.Object);

        var act = () => sut.EnsureCanCreateAgent(AgentRank.Worker);
        act.Should().Throw<InvalidOperationException>().WithMessage("*exceeded agent quota*");
    }

    [Fact]
    public void EnsureCanCreateAgent_WhenBelowTenantQuota_DoesNotThrow()
    {
        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);
        registry.Register(MockAgent("a1"));

        var tenantIsolation = new Mock<ITenantIsolationService>();
        tenantIsolation
            .Setup(t => t.GetCurrentTenant())
            .Returns(new TenantContext
            {
                TenantId = "t1",
                Name = "Tenant 1",
                MaxAgents = 2,
                IsActive = true
            });

        var sut = new TenantAgentQuotaService(registry, NullLogger<TenantAgentQuotaService>.Instance, tenantIsolation.Object);

        sut.Invoking(service => service.EnsureCanCreateAgent(AgentRank.Worker)).Should().NotThrow();
    }

    private static IAgent MockAgent(string id)
    {
        var mock = new Mock<IAgent>();
        mock.SetupGet(agent => agent.Id).Returns(id);
        mock.SetupGet(agent => agent.Name).Returns(id);
        mock.SetupGet(agent => agent.Rank).Returns(AgentRank.Worker);
        mock.SetupGet(agent => agent.Status).Returns(AgentStatus.Idle);
        return mock.Object;
    }
}