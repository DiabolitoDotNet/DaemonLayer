using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class ToolRegistrationServiceTests
{
    [Fact]
    public async Task StartAsync_RegistersAllTools()
    {
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var logger = new Mock<ILogger<ToolRegistrationService>>();

        var tool1 = new Mock<ITool>(MockBehavior.Strict);
        tool1.SetupGet(t => t.Name).Returns("tool-1");

        var tool2 = new Mock<ITool>(MockBehavior.Strict);
        tool2.SetupGet(t => t.Name).Returns("tool-2");

        registry.Setup(r => r.RegisterTool(tool1.Object));
        registry.Setup(r => r.RegisterTool(tool2.Object));

        var service = new ToolRegistrationService(registry.Object, new[] { tool1.Object, tool2.Object }, logger.Object);

        await service.StartAsync(default);

        registry.Verify(r => r.RegisterTool(tool1.Object), Times.Once);
        registry.Verify(r => r.RegisterTool(tool2.Object), Times.Once);
    }

    [Fact]
    public async Task StopAsync_DoesNothing()
    {
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var logger = new Mock<ILogger<ToolRegistrationService>>();

        var service = new ToolRegistrationService(registry.Object, Array.Empty<ITool>(), logger.Object);

        await service.StopAsync(default);

        true.Should().BeTrue();
    }
}
