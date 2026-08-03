using System.Net;
using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests.E2E;

[Collection("Host E2E")]
public sealed class AutonomyApiE2ETests
{
    [Fact]
    public async Task AutonomyReadiness_WithOperatorKey_ReturnsContractShape()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var operatorOptions = factory.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, operatorOptions.ApiKey);

        var response = await client.GetAsync("/api/autonomy/readiness");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);

        json.RootElement.TryGetProperty("generatedAtUtc", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("catalogVersion", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("allCriticalReady", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);

        if (items.GetArrayLength() > 0)
        {
            var first = items[0];
            first.TryGetProperty("capability", out _).Should().BeTrue();
            first.TryGetProperty("ready", out _).Should().BeTrue();
            first.TryGetProperty("toolRegistered", out _).Should().BeTrue();
            first.TryGetProperty("configurationReady", out _).Should().BeTrue();
            first.TryGetProperty("reason", out _).Should().BeTrue();
            first.TryGetProperty("configurationDependencies", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task AutonomyCertificationManifest_WithOperatorKey_ReturnsContractShape()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var operatorOptions = factory.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;
        client.DefaultRequestHeaders.Add(OperationalAuthGuard.HeaderName, operatorOptions.ApiKey);

        var response = await client.GetAsync("/api/autonomy/certification-manifest");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);

        json.RootElement.TryGetProperty("version", out var version).Should().BeTrue();
        version.GetString().Should().NotBeNullOrWhiteSpace();

        json.RootElement.TryGetProperty("requirements", out var requirements).Should().BeTrue();
        requirements.ValueKind.Should().Be(JsonValueKind.Array);
        requirements.GetArrayLength().Should().BeGreaterThan(0);

        var first = requirements[0];
        first.TryGetProperty("benchmarkId", out _).Should().BeTrue();
        first.TryGetProperty("requiredCapabilities", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AutonomyEndpoints_WithoutOperatorKey_ReturnUnauthorized()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var readiness = await client.GetAsync("/api/autonomy/readiness");
        readiness.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var manifest = await client.GetAsync("/api/autonomy/certification-manifest");
        manifest.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
