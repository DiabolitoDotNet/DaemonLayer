using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace InfernalHierarchy.Host.Tests.E2E;

[Collection("Host E2E")]
public sealed class HealthApiE2ETests
{
    [Fact]
    public async Task HealthReady_ShouldReturnActionableSummaryPayload()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.TryGetProperty("failingDependencies", out var failing).Should().BeTrue();
        failing.ValueKind.Should().Be(JsonValueKind.Array);
        json.RootElement.TryGetProperty("checks", out _).Should().BeTrue();
    }
}
