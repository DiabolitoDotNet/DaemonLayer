using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using CoreTaskStatus = InfernalHierarchy.Core.Entities.TaskStatus;

namespace InfernalHierarchy.Memory.Tests;

public sealed class LiteDbSharedMemoryTests : IDisposable
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
    public async Task SearchDecisionsAsync_ShouldSearchAcrossContextActionReasoning()
    {
        // Arrange
        await _memory.AddDecisionAsync(new Decision
        {
            CreatedBy = "a",
            Context = "context-needle",
            Action = "act",
            Reasoning = "why"
        });

        await _memory.AddDecisionAsync(new Decision
        {
            CreatedBy = "b",
            Context = "context",
            Action = "action-needle",
            Reasoning = "why"
        });

        await _memory.AddDecisionAsync(new Decision
        {
            CreatedBy = "c",
            Context = "context",
            Action = "act",
            Reasoning = "reason-needle"
        });

        // Act
        var byContext = await _memory.SearchDecisionsAsync("needle");
        var byAction = await _memory.SearchDecisionsAsync("action-needle");
        var byReason = await _memory.SearchDecisionsAsync("reason-needle");

        // Assert
        byContext.Should().HaveCount(3);
        byAction.Should().HaveCount(1);
        byReason.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteDecisionAsync_ShouldDelete_WhenExists_AndNoThrow_WhenMissing()
    {
        // Arrange
        var decision = new Decision
        {
            CreatedBy = "lucifer",
            Context = "ctx",
            Action = "act",
            Reasoning = "why"
        };

        await _memory.AddDecisionAsync(decision);

        // Act
        await _memory.DeleteDecisionAsync(decision.Id);
        await _memory.DeleteDecisionAsync("missing");

        // Assert
        var retrieved = await _memory.GetDecisionAsync(decision.Id);
        retrieved.Should().BeNull();
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

    [Fact]
    public async Task UpdateFactAsync_ShouldCreateVersionHistoryAndIncrementVersion()
    {
        // Arrange
        var fact = new Fact
        {
            Category = "system",
            Content = "v1",
            Source = "test",
            Confidence = 0.9,
            CreatedBy = "lucifer"
        };

        await _memory.AddFactAsync(fact);

        var updated = new Fact
        {
            Id = fact.Id,
            Category = fact.Category,
            Content = "v2",
            Source = fact.Source,
            Confidence = 0.8,
            CreatedBy = fact.CreatedBy,
            Visibility = fact.Visibility,
            SharedWithAgents = fact.SharedWithAgents,
            MinimumRankToView = fact.MinimumRankToView,
            Version = fact.Version,
            VersionHistory = new List<FactVersion>()
        };

        // Act
        await _memory.UpdateFactAsync(updated, changeReason: "edit");
        var retrieved = await _memory.GetFactAsync(fact.Id);
        var history = await _memory.GetFactHistoryAsync(fact.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Version.Should().Be(2);
        retrieved.Content.Should().Be("v2");
        history.Should().HaveCount(1);
        history.First().Content.Should().Be("v1");
        history.First().ChangeReason.Should().Be("edit");
    }

    [Fact]
    public async Task GetFactHistoryAsync_WhenMissing_ShouldReturnEmpty()
    {
        // Act
        var history = await _memory.GetFactHistoryAsync("missing");

        // Assert
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task TaskCrud_ShouldPersistUpdateAndQueryByAgentAndStatus()
    {
        // Arrange
        var task = new TaskEntry
        {
            Description = "do the thing",
            AssignedTo = "vassago",
            Status = CoreTaskStatus.Pending,
            CreatedBy = "lucifer"
        };

        await _memory.AddTaskAsync(task);

        // Act
        var retrieved = await _memory.GetTaskAsync(task.Id);
        retrieved.Should().NotBeNull();

        task.Status = CoreTaskStatus.InProgress;
        await _memory.UpdateTaskAsync(task);

        var byAgent = await _memory.GetTasksByAgentAsync("vassago");
        var byStatus = await _memory.GetTasksByStatusAsync(CoreTaskStatus.InProgress);

        await _memory.DeleteTaskAsync(task.Id);
        await _memory.DeleteTaskAsync("missing");
        var afterDelete = await _memory.GetTaskAsync(task.Id);

        // Assert
        byAgent.Should().ContainSingle(t => t.Id == task.Id);
        byStatus.Should().ContainSingle(t => t.Id == task.Id);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task GetVisibleFactsAsync_ShouldApplyVisibilityRules()
    {
        // Arrange
        var requesterId = "duke_1";
        var requesterRank = AgentRank.Duke;

        await _memory.AddFactAsync(new Fact
        {
            Category = "vis",
            Content = "public",
            Source = "test",
            CreatedBy = "someone",
            Visibility = MemoryVisibility.Public
        });

        await _memory.AddFactAsync(new Fact
        {
            Category = "vis",
            Content = "private",
            Source = "test",
            CreatedBy = "other",
            Visibility = MemoryVisibility.Private
        });

        await _memory.AddFactAsync(new Fact
        {
            Category = "vis",
            Content = "shared",
            Source = "test",
            CreatedBy = "other",
            Visibility = MemoryVisibility.Shared,
            SharedWithAgents = new List<string> { requesterId }
        });

        await _memory.AddFactAsync(new Fact
        {
            Category = "vis",
            Content = "rank-based",
            Source = "test",
            CreatedBy = "other",
            Visibility = MemoryVisibility.RankBased,
            MinimumRankToView = AgentRank.Worker
        });

        await _memory.AddFactAsync(new Fact
        {
            Category = "vis",
            Content = "creator-only",
            Source = "test",
            CreatedBy = requesterId,
            Visibility = MemoryVisibility.Private
        });

        // Act
        var visible = (await _memory.GetVisibleFactsAsync(requesterId, requesterRank)).ToList();

        // Assert
        visible.Select(f => f.Content).Should().Contain(new[] { "public", "shared", "rank-based", "creator-only" });
        visible.Select(f => f.Content).Should().NotContain("private");
    }

    [Fact]
    public async Task SearchVisibleFactsAsync_ShouldFilterByVisibilityAfterSearch()
    {
        // Arrange
        var requesterId = "worker_1";
        var requesterRank = AgentRank.Worker;

        await _memory.AddFactAsync(new Fact
        {
            Category = "alpha",
            Content = "needle-public",
            Source = "test",
            CreatedBy = "x",
            Visibility = MemoryVisibility.Public
        });

        await _memory.AddFactAsync(new Fact
        {
            Category = "alpha",
            Content = "needle-private",
            Source = "test",
            CreatedBy = "y",
            Visibility = MemoryVisibility.Private
        });

        // Act
        var results = (await _memory.SearchVisibleFactsAsync("needle", requesterId, requesterRank)).ToList();

        // Assert
        results.Should().ContainSingle();
        results.Single().Content.Should().Be("needle-public");
    }

    public void Dispose()
    {
        _memory?.Dispose();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }

        GC.SuppressFinalize(this);
    }
}
