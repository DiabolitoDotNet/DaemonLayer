using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public class AgentRegistryTests
{
    private readonly Mock<ILogger<AgentRegistry>> _logger = new();

    [Fact]
    public void Register_ShouldAddAgent_AndPreventDuplicates()
    {
        var registry = new AgentRegistry(_logger.Object);

        var agent = CreateMockAgent("a1", AgentRank.Duke, AgentStatus.Idle);

        registry.Register(agent.Object);
        registry.Register(agent.Object);

        registry.Count().Should().Be(1);
        registry.IsRegistered("a1").Should().BeTrue();
        registry.GetAgent("a1").Should().NotBeNull();
    }

    [Fact]
    public void Unregister_ShouldRemoveAgent_WithoutStopping()
    {
        var registry = new AgentRegistry(_logger.Object);

        var agent = new TrackableAgent(id: "a1", name: "A", rank: AgentRank.Worker, status: AgentStatus.Idle, parentId: null);
        registry.Register(agent);

        registry.Unregister("a1");

        registry.IsRegistered("a1").Should().BeFalse();
        agent.StopCalled.Should().BeFalse();
    }

    [Fact]
    public async Task UnregisterAsync_ShouldStopAgent_AndRemoveIt_EvenIfStopThrowsAsync()
    {
        var registry = new AgentRegistry(_logger.Object);

        var agent = new TrackableAgent(id: "a1", name: "A", rank: AgentRank.Worker, status: AgentStatus.Idle, parentId: null)
        {
            ThrowOnStop = true
        };

        registry.Register(agent);

        await registry.UnregisterAsync("a1");

        registry.IsRegistered("a1").Should().BeFalse();
        agent.StopCalled.Should().BeTrue();
    }

    [Fact]
    public async Task TerminateAgentAsync_ShouldTerminateChildrenThenParentAsync()
    {
        var registry = new AgentRegistry(_logger.Object);

        var parent = new TrackableAgent(id: "p", name: "Parent", rank: AgentRank.Duke, status: AgentStatus.Thinking, parentId: null);
        var child1 = new TrackableAgent(id: "c1", name: "Child1", rank: AgentRank.Worker, status: AgentStatus.Idle, parentId: "p");
        var child2 = new TrackableAgent(id: "c2", name: "Child2", rank: AgentRank.Worker, status: AgentStatus.ActingWithTool, parentId: "p");

        registry.Register(parent);
        registry.Register(child1);
        registry.Register(child2);

        await registry.TerminateAgentAsync("p");

        registry.Count().Should().Be(0);
        parent.StopCalled.Should().BeTrue();
        child1.StopCalled.Should().BeTrue();
        child2.StopCalled.Should().BeTrue();
    }

    [Fact]
    public void GetStats_WhenEmpty_ShouldReturnZerosAndZeroAge()
    {
        var registry = new AgentRegistry(_logger.Object);

        var stats = registry.GetStats();

        stats.TotalAgents.Should().Be(0);
        stats.OldestAgentAge.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetStats_ShouldComputeCountsByRankAndStatus()
    {
        var registry = new AgentRegistry(_logger.Object);

        registry.Register(CreateMockAgent("s", AgentRank.Supreme, AgentStatus.Idle).Object);
        registry.Register(CreateMockAgent("p", AgentRank.Prince, AgentStatus.Thinking).Object);
        registry.Register(CreateMockAgent("d", AgentRank.Duke, AgentStatus.ActingWithTool).Object);
        registry.Register(CreateMockAgent("w", AgentRank.Worker, AgentStatus.Idle).Object);

        var stats = registry.GetStats();

        stats.TotalAgents.Should().Be(4);
        stats.SupremeCount.Should().Be(1);
        stats.PrinceCount.Should().Be(1);
        stats.DukeCount.Should().Be(1);
        stats.WorkerCount.Should().Be(1);

        stats.IdleCount.Should().Be(2);
        stats.ThinkingCount.Should().Be(1);
        stats.ActiveCount.Should().Be(1);

        stats.OldestAgentAge.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    private static Mock<IAgent> CreateMockAgent(string id, AgentRank rank, AgentStatus status)
    {
        var agent = new Mock<IAgent>();
        agent.SetupGet(a => a.Id).Returns(id);
        agent.SetupGet(a => a.Name).Returns(id);
        agent.SetupGet(a => a.Rank).Returns(rank);
        agent.SetupGet(a => a.Status).Returns(status);
        agent.SetupGet(a => a.Persona).Returns(new Persona { Name = id, DemonTitle = id, SystemPrompt = "", Specializations = [] });
        agent.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return agent;
    }

    private sealed class TrackableAgent : BaseAgent
    {
        public bool StopCalled { get; private set; }
        public bool ThrowOnStop { get; set; }

        public TrackableAgent(string id, string name, AgentRank rank, AgentStatus status, string? parentId)
            : base(
                new Agent
                {
                    Id = id,
                    Name = name,
                    Rank = rank,
                    ParentAgentId = parentId,
                    PersonaPath = "souls/generic_worker.json",
                    Status = status,
                    CreatedAt = DateTime.UtcNow
                },
                new Persona { Name = name, DemonTitle = name, SystemPrompt = "", Specializations = [] },
                Mock.Of<IMessageBus>(),
                Mock.Of<ISharedMemory>(),
                Mock.Of<IToolRegistry>(),
                Mock.Of<ILogger<BaseAgent>>())
        {
            Status = status;
        }

        public override Task StopAsync(CancellationToken ct = default)
        {
            StopCalled = true;
            if (ThrowOnStop)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.CompletedTask;
        }

        public override Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
            => Task.FromResult(new AgentMessage { FromAgentId = Id, ToAgentId = task.FromAgentId, Type = MessageType.Report, Content = "ok" });
    }
}
