using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Observability;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AutonomyCertificationManifestTests
{
    [Fact]
    public void CertificationManifest_ShouldBeCoveredByReadinessCatalogAndBenchmarks()
    {
        var readiness = new AutonomyReadinessOptions();
        var readinessSet = readiness.CriticalCapabilities
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scorecard = new AutonomyScorecardService(new InfernalHierarchy.Host.Tools.AgentPlaygroundService());
        var benchmarkSet = scorecard.GetBenchmarks()
            .Select(b => b.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AutonomyCertificationManifest.Requirements.Should().NotBeEmpty();

        foreach (var requirement in AutonomyCertificationManifest.Requirements)
        {
            benchmarkSet.Should().Contain(requirement.BenchmarkId);
            requirement.RequiredCapabilities.Should().NotBeEmpty();

            foreach (var capability in requirement.RequiredCapabilities)
            {
                readinessSet.Should().Contain(capability);
            }
        }
    }
}
