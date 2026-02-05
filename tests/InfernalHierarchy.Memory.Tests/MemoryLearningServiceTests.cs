using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Embeddings;
using InfernalHierarchy.Memory.Learning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public sealed class MemoryLearningServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnImmediately_WhenDisabled()
    {
        var sharedMemory = new Mock<ISharedMemory>();
        var vectorMemory = new Mock<IVectorMemory>();
        var llm = new Mock<ILlmClient>();

        using var embedding = new OnnxEmbeddingService(
            Options.Create(new OnnxEmbeddingOptions { Enabled = false }),
            Mock.Of<ILogger<OnnxEmbeddingService>>());

        using var svc = new MemoryLearningService(
            sharedMemory.Object,
            vectorMemory.Object,
            llm.Object,
            embedding,
            Options.Create(new MemoryLearningOptions { Enabled = false }),
            Mock.Of<ILogger<MemoryLearningService>>());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await InvokePrivateAsync(svc, "ExecuteAsync", cts.Token);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldCompressLongFacts_AndClusterPublicFacts()
    {
        var longFact = new Fact
        {
            Id = "f-long",
            Content = "0123456789",
            Category = "notes",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            Confidence = 0.9
        };

        var public1 = new Fact
        {
            Id = "p1",
            Content = "public one",
            Category = "a",
            Visibility = MemoryVisibility.Public,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            Confidence = 0.8
        };

        var public2 = new Fact
        {
            Id = "p2",
            Content = "public two",
            Category = "b",
            Visibility = MemoryVisibility.Public,
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            Confidence = 0.6
        };

        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory
            .Setup(m => m.SearchFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { longFact, public1, public2 });

        sharedMemory
            .Setup(m => m.UpdateFactAsync(It.IsAny<Fact>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vectorMemory = new Mock<IVectorMemory>();
        vectorMemory
            .Setup(v => v.IndexFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var llm = new Mock<ILlmClient>();
        llm
            .Setup(l => l.GetSimpleCompletionAsync(It.Is<string>(s => s.Contains("compressing")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("short");

        llm
            .Setup(l => l.GetSimpleCompletionAsync(It.Is<string>(s => s.Contains("consolidating")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("summary");

        using var embedding = new OnnxEmbeddingService(
            Options.Create(new OnnxEmbeddingOptions { Enabled = false }),
            Mock.Of<ILogger<OnnxEmbeddingService>>());

        var options = new MemoryLearningOptions
        {
            Enabled = true,
            EnableCompression = true,
            CompressIfLongerThanChars = 5,
            CompressToMaxChars = 6,
            MaxFactsPerRun = 10,
            EnableClustering = true,
            MinClusterSize = 2,
            ClusterSimilarityThreshold = -1,
            SummaryMaxChars = 100,
            SummaryCategory = "cluster_summary"
        };

        using var svc = new MemoryLearningService(
            sharedMemory.Object,
            vectorMemory.Object,
            llm.Object,
            embedding,
            Options.Create(options),
            Mock.Of<ILogger<MemoryLearningService>>());

        await InvokePrivateAsync(svc, "RunOnceAsync", CancellationToken.None);

        sharedMemory.Verify(
            m => m.UpdateFactAsync(It.Is<Fact>(f => f.Id == "f-long" && f.Content == "short"), "Automatic compression", It.IsAny<CancellationToken>()),
            Times.Once);

        vectorMemory.Verify(v => v.IndexFactAsync(It.Is<Fact>(f => f.Category == "cluster_summary" && f.Content.Contains("summary")), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task InvokePrivateAsync(object target, string methodName, CancellationToken ct)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"Expected private method {methodName}");

        var task = (Task)method!.Invoke(target, new object[] { ct })!;
        await task.ConfigureAwait(false);
    }
}
