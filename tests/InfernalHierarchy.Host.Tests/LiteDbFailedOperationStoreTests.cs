using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Memory.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class LiteDbFailedOperationStoreTests
{
    [Fact]
    public async Task RecordAsync_ShouldPersistAcrossStoreRecreation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"failed_ops_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var failedOpsPath = Path.Combine(tempDir, "failed-operations.db");
        var memoryPath = Path.Combine(tempDir, "memory.db");

        try
        {
            var options = Options.Create(new FailedOperationHandlingOptions
            {
                Enabled = true,
                ReplayRetryBudget = 3,
                MaxEntries = 100,
                DatabasePath = failedOpsPath
            });

            var memoryOptions = Options.Create(new MemoryOptions
            {
                DatabasePath = memoryPath
            });

            var metrics = new MetricsCollector();

            using (var store = new LiteDbFailedOperationStore(
                options,
                memoryOptions,
                metrics,
                NullLogger<LiteDbFailedOperationStore>.Instance))
            {
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
            }

            using var reopened = new LiteDbFailedOperationStore(
                options,
                memoryOptions,
                metrics,
                NullLogger<LiteDbFailedOperationStore>.Instance);

            var record = await reopened.GetByIdAsync("dl-1");
            record.Should().NotBeNull();
            record!.Status.Should().Be(FailedOperationStatus.Pending);
            record.OperationName.Should().Be("message_bus_publish");

            var stats = reopened.GetStats();
            stats.Total.Should().Be(1);
            stats.Pending.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryStartReplayAsync_ShouldPersistReplayAttemptMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"failed_ops_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var failedOpsPath = Path.Combine(tempDir, "failed-operations.db");

        try
        {
            var options = Options.Create(new FailedOperationHandlingOptions
            {
                Enabled = true,
                ReplayRetryBudget = 2,
                MaxEntries = 100,
                DatabasePath = failedOpsPath
            });

            var memoryOptions = Options.Create(new MemoryOptions
            {
                DatabasePath = Path.Combine(tempDir, "memory.db")
            });

            using var store = new LiteDbFailedOperationStore(
                options,
                memoryOptions,
                new MetricsCollector(),
                NullLogger<LiteDbFailedOperationStore>.Instance);

            await store.RecordAsync(new FailedOperationRecord
            {
                Id = "dl-2",
                Kind = FailedOperationKind.ToolExecution,
                ReasonCode = "tool_exception",
                OperationName = "tool_x",
                PayloadJson = "{}"
            });

            var started = await store.TryStartReplayAsync("dl-2", "tester");
            started.Should().NotBeNull();
            started!.ReplayAttempts.Should().Be(1);
            started.Metadata.Should().ContainKey("replay_requested_by");

            var persisted = await store.GetByIdAsync("dl-2");
            persisted.Should().NotBeNull();
            persisted!.ReplayAttempts.Should().Be(1);
            persisted.Metadata["replay_requested_by"].Should().Be("tester");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReplayFailureLifecycle_ShouldPersistAcrossReopen_AndRespectRetryBudget()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"failed_ops_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var failedOpsPath = Path.Combine(tempDir, "failed-operations.db");

        try
        {
            var options = Options.Create(new FailedOperationHandlingOptions
            {
                Enabled = true,
                ReplayRetryBudget = 2,
                MaxEntries = 100,
                DatabasePath = failedOpsPath
            });

            var memoryOptions = Options.Create(new MemoryOptions
            {
                DatabasePath = Path.Combine(tempDir, "memory.db")
            });

            using (var store = new LiteDbFailedOperationStore(
                options,
                memoryOptions,
                new MetricsCollector(),
                NullLogger<LiteDbFailedOperationStore>.Instance))
            {
                await store.RecordAsync(new FailedOperationRecord
                {
                    Id = "dl-3",
                    Kind = FailedOperationKind.ToolExecution,
                    ReasonCode = "tool_exception",
                    OperationName = "tool_x",
                    PayloadJson = "{}"
                });

                var first = await store.TryStartReplayAsync("dl-3", "worker-1");
                first.Should().NotBeNull();
                await store.MarkReplayFailedAsync("dl-3", "replay_exception", "transient network issue");
            }

            using (var reopened = new LiteDbFailedOperationStore(
                options,
                memoryOptions,
                new MetricsCollector(),
                NullLogger<LiteDbFailedOperationStore>.Instance))
            {
                var persisted = await reopened.GetByIdAsync("dl-3");
                persisted.Should().NotBeNull();
                persisted!.Status.Should().Be(FailedOperationStatus.Pending);
                persisted.ReplayAttempts.Should().Be(1);

                var second = await reopened.TryStartReplayAsync("dl-3", "worker-2");
                second.Should().NotBeNull();
                await reopened.MarkReplayFailedAsync("dl-3", "unsupported_kind", "permanent failure");
            }

            using var finalReopen = new LiteDbFailedOperationStore(
                options,
                memoryOptions,
                new MetricsCollector(),
                NullLogger<LiteDbFailedOperationStore>.Instance);

            var finalRecord = await finalReopen.GetByIdAsync("dl-3");
            finalRecord.Should().NotBeNull();
            finalRecord!.Status.Should().Be(FailedOperationStatus.ReplayFailed);
            finalRecord.ReplayAttempts.Should().Be(2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
