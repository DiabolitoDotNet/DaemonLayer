using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Maintenance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public class MemoryPruningServiceTests
{
    private readonly Mock<ISharedMemory> _mockSharedMemory;
    private readonly Mock<ILogger<MemoryPruningService>> _mockLogger;
    private readonly MemoryPruningOptions _options;

    public MemoryPruningServiceTests()
    {
        _mockSharedMemory = new Mock<ISharedMemory>();
        _mockLogger = new Mock<ILogger<MemoryPruningService>>();
        
        _options = new MemoryPruningOptions
        {
            Enabled = true,
            DryRun = false,
            PruningIntervalHours = 24,
            RetentionDays = 30,
            MinConfidenceThreshold = 0.3,
            EnableArchival = true,
            ArchivePath = "./test_archive"
        };
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ShouldNotRunPruning()
    {
        // Arrange
        _options.Enabled = false;
        var service = new MemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        await service.StopAsync(cts.Token);

        // Assert
        _mockSharedMemory.Verify(
            x => x.SearchFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PruneMemoryAsync_ShouldRemoveLowConfidenceFacts()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-40);
        var lowConfidenceFacts = new List<Fact>
        {
            new() { Id = "fact_1", Content = "Old fact", Confidence = 0.2f, CreatedAt = oldDate, CreatedBy = "agent", Category = "Test", Source = "test" },
            new() { Id = "fact_2", Content = "Another old fact", Confidence = 0.1f, CreatedAt = oldDate, CreatedBy = "agent", Category = "Test", Source = "test" }
        };

        _mockSharedMemory
            .Setup(x => x.SearchFactsAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(lowConfidenceFacts);

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        // Act
        await service.PublicPruneMemoryAsync(CancellationToken.None);

        // Assert
        _mockSharedMemory.Verify(
            x => x.DeleteFactAsync(It.IsIn("fact_1", "fact_2"), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task PruneMemoryAsync_ShouldKeepHighConfidenceFacts()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-40);
        var facts = new List<Fact>
        {
            new() { Id = "fact_1", Content = "High confidence", Confidence = 0.9f, CreatedAt = oldDate, CreatedBy = "agent", Category = "Test", Source = "test" },
            new() { Id = "fact_2", Content = "Low confidence", Confidence = 0.2f, CreatedAt = oldDate, CreatedBy = "agent", Category = "Test", Source = "test" }
        };

        _mockSharedMemory
            .Setup(x => x.SearchFactsAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        // Act
        await service.PublicPruneMemoryAsync(CancellationToken.None);

        // Assert
        _mockSharedMemory.Verify(
            x => x.DeleteFactAsync("fact_1", It.IsAny<CancellationToken>()),
            Times.Never);
        _mockSharedMemory.Verify(
            x => x.DeleteFactAsync("fact_2", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PruneMemoryAsync_ShouldRemoveCompletedTasks()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-40);
        var tasks = new List<TaskEntry>
        {
            new() { Id = "task_1", Description = "Completed task", Status = InfernalHierarchy.Core.Entities.TaskStatus.Completed, AssignedTo = "agent", CreatedAt = oldDate, CompletedAt = oldDate },
            new() { Id = "task_2", Description = "Pending task", Status = InfernalHierarchy.Core.Entities.TaskStatus.Pending, AssignedTo = "agent", CreatedAt = oldDate }
        };

        _mockSharedMemory
            .Setup(x => x.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Completed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tasks);

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        // Act
        await service.PublicPruneMemoryAsync(CancellationToken.None);

        // Assert
        _mockSharedMemory.Verify(
            x => x.DeleteTaskAsync("task_1", It.IsAny<CancellationToken>()),
            Times.Once);
        _mockSharedMemory.Verify(
            x => x.DeleteTaskAsync("task_2", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PruneMemoryAsync_WithArchivalEnabled_ShouldArchiveDecisions()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-40);
        var decisions = new List<Decision>
        {
            new()
            {
                Id = "dec_1",
                Context = "Old decision",
                Action = "Archive me",
                Outcome = "Success",
                CreatedBy = "agent",
                CreatedAt = oldDate
            }
        };

        _mockSharedMemory
            .Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decisions);

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        // Act
        await service.PublicPruneMemoryAsync(CancellationToken.None);

        // Assert - Should move to archive and delete from memory
        _mockSharedMemory.Verify(
            x => x.DeleteDecisionAsync("dec_1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PruneMemoryAsync_WithArchivalDisabled_ShouldNotArchive()
    {
        // Arrange
        _options.EnableArchival = false;
        var oldDate = DateTime.UtcNow.AddDays(-40);
        var decisions = new List<Decision>
        {
            new()
            {
                Id = "dec_1",
                Context = "Old decision",
                Action = "Don't archive",
                Outcome = "Success",
                CreatedBy = "agent",
                CreatedAt = oldDate
            }
        };

        _mockSharedMemory
            .Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decisions);

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        // Act
        await service.PublicPruneMemoryAsync(CancellationToken.None);

        // Assert - Should not attempt to delete
        _mockSharedMemory.Verify(
            x => x.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PruneMemoryAsync_ShouldHandleEmptyCollections()
    {
        // Arrange
        _mockSharedMemory
            .Setup(x => x.SearchFactsAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Fact>());
        _mockSharedMemory
            .Setup(x => x.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Completed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskEntry>());
        _mockSharedMemory
            .Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Decision>());

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        // Act
        var exception = await Record.ExceptionAsync(() =>
            service.PublicPruneMemoryAsync(CancellationToken.None));

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public async Task PruneMemoryAsync_WhenDryRunEnabled_ShouldNotDeleteButShouldLogWouldPrune()
    {
        // Arrange
        _options.DryRun = true;
        _options.EnableArchival = true;
        _options.ArchivePath = "./test_archive";

        var oldDate = DateTime.UtcNow.AddDays(-40);

        _mockSharedMemory
            .Setup(x => x.SearchFactsAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Fact>
            {
                new() { Id = "fact_1", Content = "Old low confidence", Confidence = 0.1f, CreatedAt = oldDate, CreatedBy = "agent", Category = "Test", Source = "test" }
            });

        _mockSharedMemory
            .Setup(x => x.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Completed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskEntry>
            {
                new() { Id = "task_1", Description = "Completed", Status = InfernalHierarchy.Core.Entities.TaskStatus.Completed, AssignedTo = "agent", CreatedAt = oldDate, CompletedAt = oldDate }
            });

        _mockSharedMemory
            .Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Decision>
            {
                new() { Id = "dec_1", Context = "Old", Action = "Archive", Outcome = "Success", CreatedBy = "agent", CreatedAt = oldDate }
            });

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        // Act
        await service.PublicPruneMemoryAsync(CancellationToken.None);

        // Assert - No deletes in dry-run
        _mockSharedMemory.Verify(x => x.DeleteFactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockSharedMemory.Verify(x => x.DeleteTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockSharedMemory.Verify(x => x.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Assert - Logs should indicate dry-run completion
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("dry-run", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task PruneMemoryAsync_WhenSearchFactsThrows_ShouldNotThrow()
    {
        _options.EnableArchival = false;

        _mockSharedMemory
            .Setup(x => x.SearchFactsAsync("", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        _mockSharedMemory
            .Setup(x => x.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Completed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskEntry>());

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        var act = async () => await service.PublicPruneMemoryAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        _mockSharedMemory.Verify(x => x.DeleteFactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PruneMemoryAsync_WhenGetTasksThrows_ShouldNotThrow()
    {
        _options.EnableArchival = false;

        _mockSharedMemory
            .Setup(x => x.SearchFactsAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Fact>());

        _mockSharedMemory
            .Setup(x => x.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Completed, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        var act = async () => await service.PublicPruneMemoryAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        _mockSharedMemory.Verify(x => x.DeleteTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PruneMemoryAsync_WhenArchivePathInvalid_ShouldNotThrow_AndShouldNotDeleteDecision()
    {
        _options.EnableArchival = true;
        _options.ArchivePath = "invalid\0path";
        var oldDate = DateTime.UtcNow.AddDays(-40);

        _mockSharedMemory
            .Setup(x => x.SearchFactsAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Fact>());

        _mockSharedMemory
            .Setup(x => x.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Completed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskEntry>());

        _mockSharedMemory
            .Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Decision>
            {
                new()
                {
                    Id = "dec_1",
                    Context = "Old decision",
                    Action = "Archive me",
                    Outcome = "Success",
                    CreatedBy = "agent",
                    CreatedAt = oldDate
                }
            });

        var service = new TestableMemoryPruningService(
            _mockSharedMemory.Object,
            Options.Create(_options),
            _mockLogger.Object);

        var act = async () => await service.PublicPruneMemoryAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        _mockSharedMemory.Verify(x => x.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Helper class to expose protected method for testing
    private sealed class TestableMemoryPruningService : MemoryPruningService
    {
        public TestableMemoryPruningService(
            ISharedMemory sharedMemory,
            IOptions<MemoryPruningOptions> options,
            ILogger<MemoryPruningService> logger)
            : base(sharedMemory, options, logger)
        {
        }

        public async Task PublicPruneMemoryAsync(CancellationToken ct)
        {
            // Use reflection to call private method
            var method = typeof(MemoryPruningService).GetMethod(
                "PruneMemoryAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method != null)
            {
                await (Task)method.Invoke(this, new object[] { ct })!;
            }
        }
    }
}
