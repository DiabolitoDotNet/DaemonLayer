using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class GetAgentStatusToolTests
{
    private sealed class FakeAgent : IAgent
    {
        public FakeAgent(string id, string name, AgentRank rank, AgentStatus status)
        {
            Id = id;
            Name = name;
            Rank = rank;
            Status = status;
            Persona = new Persona { Name = name };
        }

        public string Id { get; }
        public string Name { get; }
        public AgentRank Rank { get; }
        public AgentStatus Status { get; }
        public Persona Persona { get; }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
            => Task.FromResult(new AgentMessage());
        public bool CanCreateSubAgent(AgentRank targetRank) => false;
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsJsonWithOccupancyAndWorkingOn()
    {
        var agents = new List<IAgent>
        {
            new FakeAgent("a1", "Lucifer", AgentRank.Supreme, AgentStatus.Thinking),
            new FakeAgent("a2", "Baal", AgentRank.Prince, AgentStatus.Idle)
        };

        var registry = new Mock<IAgentRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetAllAgents()).Returns(agents);

        var memory = new Mock<ISharedMemory>(MockBehavior.Strict);
        memory.Setup(m => m.GetTasksByAgentAsync("a1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TaskEntry
                {
                    Id = "t1",
                    AssignedTo = "a1",
                    Description = "Investigate latency",
                    Status = InfernalHierarchy.Core.Entities.TaskStatus.InProgress,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                    CreatedBy = "a1"
                }
            });
        memory.Setup(m => m.GetTasksByAgentAsync("a2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TaskEntry>());

        var tool = new InfernalHierarchy.Tools.Tools.Agent.GetAgentStatusTool(
            NullLogger<InfernalHierarchy.Tools.Tools.Agent.GetAgentStatusTool>.Instance,
            registry.Object,
            memory.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("total_agents").GetInt32().Should().Be(2);

        var agentsEl = doc.RootElement.GetProperty("agents");
        agentsEl.GetArrayLength().Should().Be(2);

        var lucifer = agentsEl.EnumerateArray().Single(a => a.GetProperty("id").GetString() == "a1");
        lucifer.GetProperty("occupied").GetBoolean().Should().BeTrue();
        lucifer.GetProperty("working_on").ValueKind.Should().Be(JsonValueKind.Object);
        lucifer.GetProperty("working_on").GetProperty("id").GetString().Should().Be("t1");

        var baal = agentsEl.EnumerateArray().Single(a => a.GetProperty("id").GetString() == "a2");
        baal.GetProperty("occupied").GetBoolean().Should().BeFalse();
        baal.GetProperty("working_on").ValueKind.Should().Be(JsonValueKind.Null);

        memory.Verify(m => m.GetTasksByAgentAsync("a1", It.IsAny<CancellationToken>()), Times.Once);
        memory.Verify(m => m.GetTasksByAgentAsync("a2", It.IsAny<CancellationToken>()), Times.Once);
        registry.Verify(r => r.GetAllAgents(), Times.Once);
    }
}
