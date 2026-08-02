using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AutonomousIncidentResponseServiceTests
{
    [Fact]
    public async Task ExecuteCycleAsync_WhenTimeoutSpikeDetected_ShouldThrottleAndRequestReplan()
    {
        var supervisor = new RecordingSupervisor();
        var registry = new InMemoryAgentRegistryStub();
        var metrics = new MetricsCollector();
        var throttle = new IncidentToolThrottleState();

        var options = Options.Create(new AutonomousIncidentResponseOptions
        {
            Enabled = true,
            ActionCooldown = TimeSpan.Zero,
            ToolTimeoutSpikeThreshold = 2,
            QueueRejectGrowthThreshold = 99,
            StalledBranchDetectionThreshold = 99,
            LoopingBranchDetectionThreshold = 99,
            RootAgentId = "lucifer",
            EnableTemporaryRateReduction = true,
            RateReductionDuration = TimeSpan.FromSeconds(45),
            DeferredToolNames = ["request_collaboration"]
        });

        var sut = new AutonomousIncidentResponseService(
            supervisor,
            registry,
            metrics,
            throttle,
            options,
            NullLogger<AutonomousIncidentResponseService>.Instance);

        await sut.ExecuteCycleAsync(); // baseline init

        metrics.IncrementCounter("tools.timeout.total", 3);
        await sut.ExecuteCycleAsync();

        supervisor.ReplanRequests.Should().ContainSingle();
        supervisor.ReplanRequests[0].RootAgentId.Should().Be("lucifer");

        var active = throttle.TryGetActiveThrottle(DateTimeOffset.UtcNow, out var snapshot);
        active.Should().BeTrue();
        snapshot.DeferredToolNames.Should().Contain("request_collaboration");

        metrics.GetCounter("incident_response.actions.rate_reduction").Should().Be(1);
        metrics.GetCounter("incident_response.actions.replan").Should().Be(1);
    }

    [Fact]
    public async Task ExecuteCycleAsync_WhenLoopingSpikeDetected_ShouldPreemptOneNonRootBranch()
    {
        var supervisor = new RecordingSupervisor();
        var registry = new InMemoryAgentRegistryStub();
        registry.Register(new TestAgent("lucifer", "Lucifer", AgentRank.Supreme, AgentStatus.Thinking));
        registry.Register(new TestAgent("worker-1", "Worker One", AgentRank.Worker, AgentStatus.Thinking));

        var metrics = new MetricsCollector();
        var throttle = new IncidentToolThrottleState();

        var options = Options.Create(new AutonomousIncidentResponseOptions
        {
            Enabled = true,
            ActionCooldown = TimeSpan.Zero,
            ToolTimeoutSpikeThreshold = 99,
            QueueRejectGrowthThreshold = 99,
            StalledBranchDetectionThreshold = 99,
            LoopingBranchDetectionThreshold = 1,
            RootAgentId = "lucifer",
            EnableBranchPreemption = true
        });

        var sut = new AutonomousIncidentResponseService(
            supervisor,
            registry,
            metrics,
            throttle,
            options,
            NullLogger<AutonomousIncidentResponseService>.Instance);

        await sut.ExecuteCycleAsync(); // baseline init

        metrics.IncrementCounter("supervisor.detected.looping", 2);
        await sut.ExecuteCycleAsync();

        supervisor.PreemptRequests.Should().ContainSingle();
        supervisor.PreemptRequests[0].AgentId.Should().Be("worker-1");
        supervisor.ReplanRequests.Should().ContainSingle();
        metrics.GetCounter("incident_response.actions.preempt").Should().Be(1);
    }

    private sealed class RecordingSupervisor : IAgentSupervisor
    {
        public List<(string RootAgentId, string Reason)> ReplanRequests { get; } = new();
        public List<(string AgentId, string Reason)> PreemptRequests { get; } = new();

        public Task RequestReplanAsync(string rootAgentId, string reason, CancellationToken ct = default)
        {
            ReplanRequests.Add((rootAgentId, reason));
            return Task.CompletedTask;
        }

        public Task PreemptAgentAsync(string agentId, string reason, CancellationToken ct = default)
        {
            PreemptRequests.Add((agentId, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAgentRegistryStub : IAgentRegistry
    {
        private readonly Dictionary<string, IAgent> _agents = new(StringComparer.OrdinalIgnoreCase);

        public void Register(IAgent agent) => _agents[agent.Id] = agent;

        public Task UnregisterAsync(string agentId, CancellationToken ct = default)
        {
            _agents.Remove(agentId);
            return Task.CompletedTask;
        }

        public void Unregister(string agentId) => _agents.Remove(agentId);

        public IAgent? GetAgent(string agentId) => _agents.TryGetValue(agentId, out var agent) ? agent : null;

        public IEnumerable<IAgent> GetAllAgents() => _agents.Values;

        public IEnumerable<IAgent> GetAgentsByRank(AgentRank rank) => _agents.Values.Where(a => a.Rank == rank);

        public IEnumerable<IAgent> GetChildAgents(string parentId) => Array.Empty<IAgent>();

        public int Count() => _agents.Count;

        public bool IsRegistered(string agentId) => _agents.ContainsKey(agentId);
    }

    private sealed class TestAgent : IAgent
    {
        public TestAgent(string id, string name, AgentRank rank, AgentStatus status)
        {
            Id = id;
            Name = name;
            Rank = rank;
            Status = status;
        }

        public string Id { get; }
        public string Name { get; }
        public AgentRank Rank { get; }
        public AgentStatus Status { get; }
        public Persona Persona { get; } = new();

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default) => Task.FromResult(task);
        public bool CanCreateSubAgent(AgentRank targetRank) => true;
    }
}
