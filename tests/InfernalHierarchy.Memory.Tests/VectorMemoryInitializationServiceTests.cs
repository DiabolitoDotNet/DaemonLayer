using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Vector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public sealed class VectorMemoryInitializationServiceTests
{
    [Fact]
    public async Task StartAsync_ShouldSkip_WhenDisabled()
    {
        var vectorMemory = new Mock<IVectorMemory>(MockBehavior.Strict);

        var svc = new VectorMemoryInitializationService(
            vectorMemory.Object,
            Options.Create(new VectorMemoryOptions { Enabled = false }),
            Mock.Of<ILogger<VectorMemoryInitializationService>>());

        await svc.StartAsync(CancellationToken.None);

        vectorMemory.Verify(v => v.InitializeCollectionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ShouldCallInitialize_WhenEnabled()
    {
        var vectorMemory = new Mock<IVectorMemory>();
        vectorMemory
            .Setup(v => v.InitializeCollectionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = new VectorMemoryInitializationService(
            vectorMemory.Object,
            Options.Create(new VectorMemoryOptions { Enabled = true }),
            Mock.Of<ILogger<VectorMemoryInitializationService>>());

        await svc.StartAsync(CancellationToken.None);

        vectorMemory.Verify(v => v.InitializeCollectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldNotThrow_WhenInitializeFails()
    {
        var vectorMemory = new Mock<IVectorMemory>();
        vectorMemory
            .Setup(v => v.InitializeCollectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var svc = new VectorMemoryInitializationService(
            vectorMemory.Object,
            Options.Create(new VectorMemoryOptions { Enabled = true }),
            Mock.Of<ILogger<VectorMemoryInitializationService>>());

        var act = async () => await svc.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_ShouldBeNoOp()
    {
        var vectorMemory = new Mock<IVectorMemory>(MockBehavior.Strict);

        var svc = new VectorMemoryInitializationService(
            vectorMemory.Object,
            Options.Create(new VectorMemoryOptions { Enabled = false }),
            Mock.Of<ILogger<VectorMemoryInitializationService>>());

        await svc.StopAsync(CancellationToken.None);
    }
}
