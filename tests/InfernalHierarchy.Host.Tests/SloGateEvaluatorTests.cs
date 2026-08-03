using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Observability;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class SloGateEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenInsufficientDataAndFailClosed_ShouldFail()
    {
        var metrics = new MetricsCollector();

        var store = new Mock<IFailedOperationStore>();
        store.Setup(x => x.GetStats()).Returns(new FailedOperationStats(0, 0, 0, 0));

        var bus = new Mock<IMessageBus>();
        var sut = new SloGateEvaluator(metrics, store.Object, bus.Object);

        var result = sut.Evaluate(new SloGateOptions
        {
            Enabled = true,
            FailOnInsufficientData = true,
            MinReplaySamples = 5,
            MinQueueSamples = 20,
            MinTaskCompletionSamples = 5,
            MinAutonomyTaskSamples = 5,
            MinAutonomyReplaySamples = 3,
            MinAutonomyTerminalSamples = 5,
        });

        result.Passed.Should().BeFalse();
        result.Checks.Should().Contain(c => c.Status == "insufficient_data" && !c.Passed);
    }

    [Fact]
    public void Evaluate_WhenAutonomyRatiosAndMedianBreachThresholds_ShouldFailAutonomyGates()
    {
        var metrics = new MetricsCollector();
        metrics.IncrementCounter("autonomy.task.in_scope_total", 1);
        metrics.SetGauge("autonomy_in_scope_task_completion_ratio", 0.2);
        metrics.SetGauge("autonomy_in_scope_terminal_failure_ratio", 0.8);
        metrics.IncrementCounter("autonomy.replay.total", 1);
        metrics.SetGauge("autonomy_replay_success_ratio", 0.0);
        metrics.RecordValue("autonomy.time_to_terminal_ms", 250);

        var store = new Mock<IFailedOperationStore>();
        store.Setup(x => x.GetStats()).Returns(new FailedOperationStats(0, 0, 0, 0));

        var bus = new Mock<IMessageBus>();
        var sut = new SloGateEvaluator(metrics, store.Object, bus.Object);

        var result = sut.Evaluate(new SloGateOptions
        {
            Enabled = true,
            MaxDeadLetterBacklogGrowth = 100,
            MinReplaySamples = 999,
            MinQueueSamples = 999,
            MinTaskCompletionSamples = 999,
            MinAutonomyTaskSamples = 1,
            MinAutonomyReplaySamples = 1,
            MinAutonomyTerminalSamples = 1,
            MinAutonomyTaskCompletionRatio = 0.95,
            MaxAutonomyTerminalFailureRatio = 0.05,
            MinAutonomyReplaySuccessRatio = 0.9,
            MaxAutonomyMedianTimeToTerminalMs = 100
        });

        result.Passed.Should().BeFalse();
        result.Checks.Should().Contain(c => c.Gate == "autonomy.task_completion_ratio" && !c.Passed);
        result.Checks.Should().Contain(c => c.Gate == "autonomy.terminal_failure_ratio" && !c.Passed);
        result.Checks.Should().Contain(c => c.Gate == "autonomy.replay_success_ratio" && !c.Passed);
        result.Checks.Should().Contain(c => c.Gate == "autonomy.median_time_to_terminal_ms" && !c.Passed);
    }

    [Fact]
    public void Evaluate_WhenOutOfScopeDominatesButInScopeHealthy_ShouldPassInScopeAutonomyGates()
    {
        var metrics = new MetricsCollector();
        metrics.IncrementCounter("autonomy.task.total", 20);
        metrics.IncrementCounter("autonomy.task.out_of_scope", 19);
        metrics.SetGauge("autonomy_task_completion_ratio", 0.05);
        metrics.SetGauge("autonomy_terminal_failure_ratio", 0.95);

        metrics.IncrementCounter("autonomy.task.in_scope_total", 1);
        metrics.IncrementCounter("autonomy.task.in_scope_completed", 1);
        metrics.SetGauge("autonomy_in_scope_task_completion_ratio", 1.0);
        metrics.SetGauge("autonomy_in_scope_terminal_failure_ratio", 0.0);

        metrics.IncrementCounter("autonomy.replay.total", 1);
        metrics.SetGauge("autonomy_replay_success_ratio", 1.0);
        metrics.RecordValue("autonomy.time_to_terminal_ms", 10);

        var store = new Mock<IFailedOperationStore>();
        store.Setup(x => x.GetStats()).Returns(new FailedOperationStats(0, 0, 0, 0));

        var bus = new Mock<IMessageBus>();
        var sut = new SloGateEvaluator(metrics, store.Object, bus.Object);

        var result = sut.Evaluate(new SloGateOptions
        {
            Enabled = true,
            MaxDeadLetterBacklogGrowth = 100,
            MinReplaySamples = 999,
            MinQueueSamples = 999,
            MinTaskCompletionSamples = 999,
            MinAutonomyTaskSamples = 1,
            MinAutonomyReplaySamples = 1,
            MinAutonomyTerminalSamples = 1,
            MinAutonomyTaskCompletionRatio = 1.0,
            MaxAutonomyTerminalFailureRatio = 0.0,
            MinAutonomyReplaySuccessRatio = 1.0,
            MaxAutonomyMedianTimeToTerminalMs = 100,
        });

        result.Checks.Should().Contain(c => c.Gate == "autonomy.task_completion_ratio" && c.Passed);
        result.Checks.Should().Contain(c => c.Gate == "autonomy.terminal_failure_ratio" && c.Passed);
    }
}