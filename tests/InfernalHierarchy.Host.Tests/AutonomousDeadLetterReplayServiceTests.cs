using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AutonomousDeadLetterReplayServiceTests
{
    [Fact]
    public async Task Worker_ShouldReplayPendingEntriesAutomatically()
    {
        var store = new InMemoryFailedOperationStore(
            Options.Create(new FailedOperationHandlingOptions
            {
                Enabled = true,
                ReplayRetryBudget = 3,
                MaxEntries = 100,
                AutonomousReplayEnabled = true,
                ReplayBatchSize = 10,
                ReplayPollIntervalMs = 50
            }),
            new MetricsCollector(),
            NullLogger<InMemoryFailedOperationStore>.Instance);

        var bus = new FakeMessageBus();
        var tools = new FakeToolRegistry();
        var replay = new DeadLetterReplayService(store, bus, tools, NullLogger<DeadLetterReplayService>.Instance);

        var message = new AgentMessage
        {
            Id = "msg-auto-1",
            FromAgentId = "sender",
            ToAgentId = "target",
            Type = MessageType.Task,
            Content = "hello"
        };

        await store.RecordAsync(new FailedOperationRecord
        {
            Id = "dl-auto-1",
            Kind = FailedOperationKind.MessagePublish,
            ReasonCode = "queue_reject",
            OperationName = "message_bus_publish",
            PayloadJson = JsonSerializer.Serialize(message)
        });

        using var worker = new AutonomousDeadLetterReplayService(
            store,
            replay,
            Options.Create(new FailedOperationHandlingOptions
            {
                Enabled = true,
                ReplayRetryBudget = 3,
                MaxEntries = 100,
                AutonomousReplayEnabled = true,
                ReplayBatchSize = 10,
                ReplayPollIntervalMs = 50
            }),
            NullLogger<AutonomousDeadLetterReplayService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.StartAsync(cts.Token);

        var replayed = false;
        for (var i = 0; i < 30; i++)
        {
            var record = await store.GetByIdAsync("dl-auto-1", cts.Token);
            if (record?.Status == FailedOperationStatus.Replayed)
            {
                replayed = true;
                break;
            }

            await Task.Delay(50, cts.Token);
        }

        await worker.StopAsync(cts.Token);

        replayed.Should().BeTrue();
        bus.Published.Should().ContainSingle(m => m.Id == "msg-auto-1");
    }

    private sealed class FakeMessageBus : IMessageBus
    {
        public List<AgentMessage> Published { get; } = new();

        public Task PublishAsync(AgentMessage message, CancellationToken ct = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<AgentMessage> SubscribeAsync(string agentId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<AgentMessage> SubscribeToBroadcastsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeToolRegistry : IToolRegistry
    {
        public void RegisterTool(ITool tool)
        {
        }

        public bool UnregisterTool(string name) => true;

        public ITool? GetTool(string name) => null;

        public IEnumerable<ITool> GetAllTools() => Enumerable.Empty<ITool>();

        public IEnumerable<ITool> GetToolsForAgent(string[] toolNames) => Enumerable.Empty<ITool>();

        public Task<ToolResult> ExecuteToolWithTrackingAsync(
            string toolName,
            Dictionary<string, object> parameters,
            string? agentId = null,
            string? agentRank = null,
            string? agentName = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(new ToolResult
            {
                Success = true,
                Output = "ok"
            });
        }
    }
}
