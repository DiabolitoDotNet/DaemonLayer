using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class HostOptionsTests
{
    [Fact]
    public static void OpenTelemetryExportOptions_HaveExpectedDefaults()
    {
        var opts = new OpenTelemetryExportOptions();

        opts.Console.Enabled.Should().BeTrue();
        opts.Otlp.Enabled.Should().BeFalse();
        opts.Otlp.Endpoint.Should().Be("http://localhost:4317");
    }

    [Fact]
    public static void HttpEndpointOptions_HaveExpectedDefaults()
    {
        var opts = new HttpEndpointOptions();

        opts.Enabled.Should().BeTrue();
        opts.Urls.Should().Be("http://localhost:5080");
    }
}
