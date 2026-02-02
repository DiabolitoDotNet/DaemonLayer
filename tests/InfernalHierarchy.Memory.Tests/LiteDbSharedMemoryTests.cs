using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public class LiteDbSharedMemoryTests : IDisposable
{
    private readonly LiteDbSharedMemory _memory;
    private readonly string _testDbPath;

    public LiteDbSharedMemoryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_infernal_{Guid.NewGuid()}.db");
        var options = Options.Create(new MemoryOptions { DatabasePath = _testDbPath });
        var logger = Mock.Of<ILogger<LiteDbSharedMemory>>();

        _memory = new LiteDbSharedMemory(options, logger);
    }

    [Fact]
    public async Task AddDecisionAsync_ShouldPersistDecision()
    {
        // Arrange
        var decision = new Decision
        {
            CreatedBy = "lucifer",
            Context = "Test context",
            Action = "Test action",
            Reasoning = "Test reasoning"
        };

        // Act
        await _memory.AddDecisionAsync(decision);
        var retrieved = await _memory.GetDecisionAsync(decision.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.CreatedBy.Should().Be("lucifer");
        retrieved.Action.Should().Be("Test action");
    }

    [Fact]
    public async Task GetRecentDecisionsAsync_ShouldReturnMostRecent()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await _memory.AddDecisionAsync(new Decision
            {
                CreatedBy = $"agent_{i}",
                Action = $"Action {i}",
                Context = "Test"
            });
            await Task.Delay(10); // Ensure different timestamps
        }

        // Act
        var recent = await _memory.GetRecentDecisionsAsync(3);

        // Assert
        recent.Should().HaveCount(3);
        recent.First().CreatedBy.Should().Be("agent_4"); // Most recent
    }

    [Fact]
    public async Task AddFactAsync_ShouldPersistFact()
    {
        // Arrange
        var fact = new Fact
        {
            Category = "system",
            Content = "Test fact content",
            Source = "test"
        };

        // Act
        await _memory.AddFactAsync(fact);
        var retrieved = await _memory.GetFactAsync(fact.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Category.Should().Be("system");
        retrieved.Content.Should().Be("Test fact content");
    }

    [Fact]
    public async Task SearchFactsAsync_ShouldFindMatchingFacts()
    {
        // Arrange
        await _memory.AddFactAsync(new Fact { Content = "The sky is blue", Category = "test" });
        await _memory.AddFactAsync(new Fact { Content = "The grass is green", Category = "test" });

        // Act
        var results = await _memory.SearchFactsAsync("blue");

        // Assert
        results.Should().HaveCount(1);
        results.First().Content.Should().Contain("blue");
    }

    public void Dispose()
    {
        _memory?.Dispose();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }
}
