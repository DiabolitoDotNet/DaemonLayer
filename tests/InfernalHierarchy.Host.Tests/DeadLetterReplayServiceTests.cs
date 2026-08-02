using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class DeadLetterReplayServiceTests
{
    [Fact]
    public async Task ReplayAsync_ForMessagePublish_ReplaysAndMarksSucceeded()
    {
        var store = CreateStore();
        var bus = new FakeMessageBus();
        var tools = new FakeToolRegistry();

        var message = new AgentMessage
        {
            Id = "msg-1",
            FromAgentId = "a",
            ToAgentId = "b",
            Type = MessageType.Task,
            Content = "hello"
        };

        await store.RecordAsync(new FailedOperationRecord
        {
            Id = "dl-1",
            Kind = FailedOperationKind.MessagePublish,
            ReasonCode = "queue_reject",
            OperationName = "message_bus_publish",
            PayloadJson = JsonSerializer.Serialize(message)
        });

        var sut = new DeadLetterReplayService(store, bus, tools, NullLogger<DeadLetterReplayService>.Instance);

        var replay = await sut.ReplayAsync("dl-1", "tester", CancellationToken.None);

        replay.Succeeded.Should().BeTrue();
        bus.Published.Should().ContainSingle(m => m.Id == "msg-1");

        var updated = await store.GetByIdAsync("dl-1", CancellationToken.None);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(FailedOperationStatus.Replayed);
        updated.ReplayAttempts.Should().Be(1);
    }

    [Fact]
    public async Task ReplayAsync_WhenRetryBudgetExhausted_ReturnsNotAvailable()
    {
        var store = CreateStore(retryBudget: 1);
        var bus = new FakeMessageBus();
        var tools = new FakeToolRegistry();

        var payload = new ToolReplayPayload
        {
            ToolName = "any",
            Parameters = new Dictionary<string, object>()
        };

        await store.RecordAsync(new FailedOperationRecord
        {
            Id = "dl-2",
            Kind = FailedOperationKind.ToolExecution,
            ReasonCode = "tool_exception",
            OperationName = "any",
            PayloadJson = JsonSerializer.Serialize(payload),
            RetryBudget = 1,
            ReplayAttempts = 1
        });

        var sut = new DeadLetterReplayService(store, bus, tools, NullLogger<DeadLetterReplayService>.Instance);

        var replay = await sut.ReplayAsync("dl-2", "tester", CancellationToken.None);

        replay.Available.Should().BeFalse();
        replay.ReasonCode.Should().Be("not_available");
    }

    [Fact]
    public async Task ReplayAsync_ForToolExecution_UsesReplayAgentAndMarksSucceeded()
    {
        var store = CreateStore();
        var bus = new FakeMessageBus();
        var tools = new FakeToolRegistry();

        var payload = new ToolReplayPayload
        {
            ToolName = "tool-x",
            Parameters = new Dictionary<string, object>
            {
                ["x"] = "1"
            },
            AgentRank = "Duke",
            AgentName = "Baal"
        };

        await store.RecordAsync(new FailedOperationRecord
        {
            Id = "dl-3",
            Kind = FailedOperationKind.ToolExecution,
            ReasonCode = "tool_result_failed",
            OperationName = "tool-x",
            PayloadJson = JsonSerializer.Serialize(payload)
        });

        var sut = new DeadLetterReplayService(store, bus, tools, NullLogger<DeadLetterReplayService>.Instance);

        var replay = await sut.ReplayAsync("dl-3", "tester", CancellationToken.None);

        replay.Succeeded.Should().BeTrue();
        tools.LastAgentId.Should().Be(FailedOperationReplayConstants.ReplayAgentId);

        var updated = await store.GetByIdAsync("dl-3", CancellationToken.None);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(FailedOperationStatus.Replayed);
    }

    private static InMemoryFailedOperationStore CreateStore(int retryBudget = 3)
    {
        return new InMemoryFailedOperationStore(
            Options.Create(new FailedOperationHandlingOptions
            {
                Enabled = true,
                ReplayRetryBudget = retryBudget,
                MaxEntries = 100
            }),
            new MetricsCollector(),
            NullLogger<InMemoryFailedOperationStore>.Instance);
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
        public string? LastAgentId { get; private set; }

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
            LastAgentId = agentId;
            return Task.FromResult(new ToolResult
            {
                Success = true,
                Output = "ok"
            });
        }
    }
}
