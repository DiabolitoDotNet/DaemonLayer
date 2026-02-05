using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using Xunit;

namespace InfernalHierarchy.Core.Tests.Entities;

public class TenantContextTests
{
    [Fact]
    public void TenantContext_ShouldInitializeWithDefaults()
    {
        var tenant = new TenantContext();

        tenant.TenantId.Should().BeEmpty();
        tenant.Name.Should().BeEmpty();
        tenant.Tier.Should().Be(TenantTier.Free);
        tenant.IsActive.Should().BeTrue();
        tenant.MaxAgents.Should().Be(10);
        tenant.MaxMemoryEntries.Should().Be(10000);
        tenant.MaxTokensPerMonth.Should().Be(1000000);
        tenant.Metadata.Should().NotBeNull();
        tenant.AllowedUserIds.Should().NotBeNull();
        tenant.DatabasePath.Should().BeNull();
        tenant.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
