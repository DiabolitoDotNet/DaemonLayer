using FluentAssertions;
using InfernalHierarchy.Agents.Orchestration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AgentStatusChangeProjectionServiceTests
{
    [Fact]
    public async Task BroadcastAgentStatusChanged_ShouldWriteFactToSharedMemory()
    {
        var bus = new InfernalHierarchy.Messaging.Bus.ChannelMessageBus(NullLogger<InfernalHierarchy.Messaging.Bus.ChannelMessageBus>.Instance);

        var memory = new Mock<ISharedMemory>(MockBehavior.Strict);
        var wrote = new TaskCompletionSource<Fact>(TaskCreationOptions.RunContinuationsAsynchronously);
        memory
            .Setup(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Callback<Fact, CancellationToken>((f, _) => wrote.TrySetResult(f))
            .Returns(Task.CompletedTask);

        var registry = new Mock<IAgentRegistry>(MockBehavior.Loose);
        registry
            .Setup(r => r.GetAgentsByRank(AgentRank.Supreme))
            .Returns(Array.Empty<IAgent>());

        var options = Options.Create(new HierarchyOptions { MainAgentName = "Lucifer" });

        using var sut = new AgentStatusChangeProjectionService(
            bus,
            memory.Object,
            registry.Object,
            options,
            NullLogger<AgentStatusChangeProjectionService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await sut.StartAsync(cts.Token);

        await bus.PublishAsync(new AgentMessage
        {
            FromAgentId = "agent-1",
            ToAgentId = null,
            Type = MessageType.Notification,
            Content = "[agent_status_changed] A (Worker) Idle -> Thinking",
            Payload = new Dictionary<string, object>
            {
                ["event"] = "agent_status_changed",
                ["agent_id"] = "agent-1",
                ["agent_name"] = "A",
                ["agent_rank"] = "Worker",
                ["from_status"] = "Idle",
                ["to_status"] = "Thinking",
                ["reason"] = "test",
                ["utc"] = DateTime.UtcNow.ToString("O")
            }
        }, cts.Token);

        var fact = await wrote.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fact.Category.Should().Be("agent_status_change");
        fact.Visibility.Should().Be(MemoryVisibility.RankBased);
        fact.MinimumRankToView.Should().Be(AgentRank.Duke);

        await sut.StopAsync(CancellationToken.None);
    }
}
