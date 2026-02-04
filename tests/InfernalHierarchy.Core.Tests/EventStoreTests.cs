using FluentAssertions;
using InfernalHierarchy.Core;
using InfernalHierarchy.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Core.Tests;

public sealed class EventStoreTests : IDisposable
{
    private readonly string _testStorePath;
    private readonly Mock<ILogger<EventStore>> _mockLogger;
    private readonly EventStore _sut;

    public EventStoreTests()
    {
        _testStorePath = Path.Combine(Path.GetTempPath(), $"test_events_{Guid.NewGuid()}");
        _mockLogger = new Mock<ILogger<EventStore>>();
        _sut = new EventStore(_testStorePath, _mockLogger.Object);
    }

    [Fact]
    public void Constructor_ShouldCreateStoreDirectory()
    {
        // Assert
        Directory.Exists(_testStorePath).Should().BeTrue();
    }

    [Fact]
    public async Task AppendEvent_ShouldStoreEventInFile()
    {
        // Arrange
        var agentEvent = new AgentEvent
        {
            AgentId = "agent_123",
            Type = EventType.AgentCreated,
            Timestamp = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["Name"] = "Lucifer",
                ["Rank"] = "Supreme"
            }
        };

        // Act
        _sut.AppendEvent(agentEvent);
        await Task.Delay(6000); // Wait for flush (5 second timer + buffer)

        // Assert
        var events = await _sut.GetAgentEventsAsync("agent_123");
        events.Should().ContainSingle();
        events.First().Type.Should().Be(EventType.AgentCreated);
    }

    [Fact]
    public async Task GetAgentEventsAsync_WithNoEvents_ShouldReturnEmpty()
    {
        // Act
        var events = await _sut.GetAgentEventsAsync("nonexistent_agent");

        // Assert
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAgentEventsAsync_ShouldReturnAllEventsForAgent()
    {
        // Arrange
        var agentId = "agent_multi";
        var events = new[]
        {
            new AgentEvent
            {
                AgentId = agentId,
                Type = EventType.AgentCreated,
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                Metadata = new Dictionary<string, object>()
            },
            new AgentEvent
            {
                AgentId = agentId,
                Type = EventType.TaskReceived,
                Timestamp = DateTime.UtcNow.AddMinutes(-3),
                Metadata = new Dictionary<string, object> { ["TaskId"] = "task_1" }
            },
            new AgentEvent
            {
                AgentId = agentId,
                Type = EventType.TaskCompleted,
                Timestamp = DateTime.UtcNow.AddMinutes(-1),
                Metadata = new Dictionary<string, object> { ["TaskId"] = "task_1" }
            }
        };

        // Act
        foreach (var evt in events)
        {
            _sut.AppendEvent(evt);
        }
        await Task.Delay(6000); // Wait for flush

        var retrievedEvents = await _sut.GetAgentEventsAsync(agentId);

        // Assert
        retrievedEvents.Should().HaveCount(3);
        retrievedEvents.Select(e => e.Type).Should().Contain(new[] { EventType.AgentCreated, EventType.TaskReceived, EventType.TaskCompleted });
    }

    [Fact]
    public async Task ReplayEventsAsync_ShouldReconstructAgentState()
    {
        // Arrange
        var agentId = "agent_replay";
        var events = new[]
        {
            new AgentEvent
            {
                AgentId = agentId,
                Type = EventType.AgentCreated,
                Timestamp = DateTime.UtcNow.AddMinutes(-10),
                Metadata = new Dictionary<string, object>
                {
                    ["Name"] = "Baal",
                    ["Rank"] = "Prince"
                }
            },
            new AgentEvent
            {
                AgentId = agentId,
                Type = EventType.MessageReceived,
                Timestamp = DateTime.UtcNow.AddMinutes(-9),
                Metadata = new Dictionary<string, object>()
            },
            new AgentEvent
            {
                AgentId = agentId,
                Type = EventType.TaskReceived,
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                Metadata = new Dictionary<string, object> { ["TaskId"] = "task_1" }
            },
            new AgentEvent
            {
                AgentId = agentId,
                Type = EventType.DecisionMade,
                Timestamp = DateTime.UtcNow.AddMinutes(-3),
                Metadata = new Dictionary<string, object> { ["Decision"] = "Execute tool" }
            }
        };

        // Act
        foreach (var evt in events)
        {
            _sut.AppendEvent(evt);
        }
        await Task.Delay(6000); // Wait for flush

        var state = await _sut.ReplayEventsAsync(agentId);

        // Assert
        state.Should().NotBeNull();
        state.AgentId.Should().Be(agentId);
        state.EventCount.Should().Be(4);
    }

    [Fact]
    public async Task GetEventsByTimeRangeAsync_ShouldFilterByTimeRange()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var agentId = "agent_timerange";

        var oldEvent = new AgentEvent
        {
            AgentId = agentId,
            Type = EventType.ToolExecuted,
            Timestamp = now.AddHours(-2),
            Description = "OldEvent",
            Metadata = new Dictionary<string, object>()
        };

        var recentEvent = new AgentEvent
        {
            AgentId = agentId,
            Type = EventType.TaskReceived,
            Timestamp = now.AddMinutes(-5),
            Description = "RecentEvent",
            Metadata = new Dictionary<string, object>()
        };

        var futureEvent = new AgentEvent
        {
            AgentId = agentId,
            Type = EventType.MessageSent,
            Timestamp = now.AddMinutes(5),
            Description = "FutureEvent",
            Metadata = new Dictionary<string, object>()
        };

        // Act
        _sut.AppendEvent(oldEvent);
        _sut.AppendEvent(recentEvent);
        _sut.AppendEvent(futureEvent);
        await Task.Delay(6000); // Wait for flush

        var eventsInRange = await _sut.GetEventsByTimeRangeAsync(
            now.AddMinutes(-10),
            now.AddMinutes(0));

        // Assert
        eventsInRange.Should().ContainSingle();
        eventsInRange.First().Type.Should().Be(EventType.TaskReceived);
    }

    [Fact]
    public async Task GetEventsByTimeRangeAsync_WithEmptyRange_ShouldReturnEmpty()
    {
        // Act
        var events = await _sut.GetEventsByTimeRangeAsync(
            DateTime.UtcNow.AddDays(-100),
            DateTime.UtcNow.AddDays(-99));

        // Assert
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task EventStore_ShouldHandleConcurrentWrites()
    {
        // Arrange
        var agentId = "agent_concurrent";
        var tasks = new List<Task>();

        // Act - Append 100 events concurrently
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var evt = new AgentEvent
                {
                    AgentId = agentId,
                    Type = EventType.ToolExecuted,
                    Timestamp = DateTime.UtcNow,
                    Description = "ConcurrentEvent",
                    Metadata = new Dictionary<string, object> { ["Index"] = index }
                };
                _sut.AppendEvent(evt);
            }));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(6000); // Wait for flush

        // Assert
        var events = await _sut.GetAgentEventsAsync(agentId);
        events.Should().HaveCount(100);
    }

    [Fact]
    public async Task AppendEvent_WithMultipleAgents_ShouldCreateSeparateFiles()
    {
        // Arrange
        var agent1 = "agent_1";
        var agent2 = "agent_2";

        var event1 = new AgentEvent
        {
            AgentId = agent1,
            Type = EventType.MessageSent,
            Timestamp = DateTime.UtcNow,
            Description = "Event1",
            Metadata = new Dictionary<string, object>()
        };

        var event2 = new AgentEvent
        {
            AgentId = agent2,
            Type = EventType.MessageReceived,
            Timestamp = DateTime.UtcNow,
            Description = "Event2",
            Metadata = new Dictionary<string, object>()
        };

        // Act
        _sut.AppendEvent(event1);
        _sut.AppendEvent(event2);
        await Task.Delay(6000); // Wait for flush

        // Assert
        var events1 = await _sut.GetAgentEventsAsync(agent1);
        var events2 = await _sut.GetAgentEventsAsync(agent2);

        events1.Should().ContainSingle();
        events1.First().Description.Should().Be("Event1");

        events2.Should().ContainSingle();
        events2.First().Description.Should().Be("Event2");
    }

    [Fact]
    public async Task ReplayEventsAsync_WithNoEvents_ShouldReturnEmptyState()
    {
        // Act
        var state = await _sut.ReplayEventsAsync("nonexistent_agent");

        // Assert
        state.Should().NotBeNull();
        state.AgentId.Should().Be("nonexistent_agent");
        state.EventCount.Should().Be(0);
    }

    [Fact]
    public async Task EventStore_ShouldPreserveEventOrdering()
    {
        // Arrange
        var agentId = "agent_ordered";
        var events = Enumerable.Range(1, 10).Select(i => new AgentEvent
        {
            AgentId = agentId,
            Type = EventType.MessageSent,
            Description = $"Event{i}",
            Timestamp = DateTime.UtcNow.AddSeconds(i),
            Metadata = new Dictionary<string, object> { ["Order"] = i }
        }).ToArray();

        // Act
        foreach (var evt in events)
        {
            _sut.AppendEvent(evt);
        }
        await Task.Delay(6000); // Wait for flush

        var retrievedEvents = (await _sut.GetAgentEventsAsync(agentId)).ToArray();

        // Assert
        retrievedEvents.Should().HaveCount(10);
        for (int i = 0; i < 10; i++)
        {
            retrievedEvents[i].Description.Should().Be($"Event{i + 1}");
        }
    }

    public void Dispose()
    {
        _sut.Dispose();

        // Cleanup test directory
        if (Directory.Exists(_testStorePath))
        {
            try
            {
                Directory.Delete(_testStorePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        GC.SuppressFinalize(this);
    }
}
