namespace InfernalHierarchy.Host.Observability;

internal sealed class SloGateEvaluator
{
    private readonly MetricsCollector _metrics;
    private readonly IFailedOperationStore _failedOperationStore;
    private readonly IMessageBus _messageBus;
    private readonly int _baselinePendingDeadLetters;

    public SloGateEvaluator(
        MetricsCollector metrics,
        IFailedOperationStore failedOperationStore,
        IMessageBus messageBus)
    {
        _metrics = metrics;
        _failedOperationStore = failedOperationStore;
        _messageBus = messageBus;
        _baselinePendingDeadLetters = failedOperationStore.GetStats().Pending;
    }

    public SloGateEvaluationResult Evaluate(SloGateOptions options)
    {
        var checks = new List<SloGateCheckResult>();

        if (!options.Enabled)
        {
            checks.Add(new SloGateCheckResult(
                Gate: "slo.enabled",
                Passed: true,
                Status: "skipped",
                Value: 0,
                Threshold: 0,
                Unit: "flag",
                Message: "SLO gates are disabled by configuration."));

            return new SloGateEvaluationResult(
                Passed: true,
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                Checks: checks);
        }

        var deadLetterStats = _failedOperationStore.GetStats();
        var pendingGrowth = Math.Max(0, deadLetterStats.Pending - _baselinePendingDeadLetters);
        checks.Add(new SloGateCheckResult(
            Gate: "deadletter.backlog_growth",
            Passed: pendingGrowth <= options.MaxDeadLetterBacklogGrowth,
            Status: "enforced",
            Value: pendingGrowth,
            Threshold: options.MaxDeadLetterBacklogGrowth,
            Unit: "count",
            Message: $"pending={deadLetterStats.Pending}, baseline={_baselinePendingDeadLetters}"));

        var replaySucceeded = _metrics.GetCounter("deadletter.replay.succeeded");
        var replayFailed = _metrics.GetCounter("deadletter.replay.failed");
        var replayAttempts = replaySucceeded + replayFailed;
        if (replayAttempts < options.MinReplaySamples)
        {
            checks.Add(new SloGateCheckResult(
                Gate: "deadletter.replay_success_ratio",
                Passed: !options.FailOnInsufficientData,
                Status: "insufficient_data",
                Value: replayAttempts,
                Threshold: options.MinReplaySamples,
                Unit: "samples",
                Message: "Not enough replay samples to enforce ratio gate yet."));
        }
        else
        {
            var replayRatio = replayAttempts == 0 ? 1d : (double)replaySucceeded / replayAttempts;
            checks.Add(new SloGateCheckResult(
                Gate: "deadletter.replay_success_ratio",
                Passed: replayRatio >= options.MinReplaySuccessRatio,
                Status: "enforced",
                Value: replayRatio,
                Threshold: options.MinReplaySuccessRatio,
                Unit: "ratio",
                Message: $"succeeded={replaySucceeded}, failed={replayFailed}"));
        }

        var rejected = 0d;
        var published = 0d;
        if (_messageBus is ChannelMessageBus bus)
        {
            rejected = bus.RejectedMessages;
            published = bus.PublishedMessages;
        }

        if (published < options.MinQueueSamples)
        {
            checks.Add(new SloGateCheckResult(
                Gate: "message_bus.reject_rate",
                Passed: !options.FailOnInsufficientData,
                Status: "insufficient_data",
                Value: published,
                Threshold: options.MinQueueSamples,
                Unit: "samples",
                Message: "Not enough published messages to enforce reject-rate gate yet."));
        }
        else
        {
            var rejectRate = published <= 0 ? 0 : rejected / published;
            checks.Add(new SloGateCheckResult(
                Gate: "message_bus.reject_rate",
                Passed: rejectRate <= options.MaxQueueRejectRate,
                Status: "enforced",
                Value: rejectRate,
                Threshold: options.MaxQueueRejectRate,
                Unit: "ratio",
                Message: $"rejected={rejected}, published={published}"));
        }

        var taskLatencyStats = _metrics.GetHistogramStats("http.latency.post.api.chat.ms");
        if (taskLatencyStats.Count < options.MinTaskCompletionSamples)
        {
            checks.Add(new SloGateCheckResult(
                Gate: "task_completion.p95_ms",
                Passed: !options.FailOnInsufficientData,
                Status: "insufficient_data",
                Value: taskLatencyStats.Count,
                Threshold: options.MinTaskCompletionSamples,
                Unit: "samples",
                Message: "Not enough /api/chat samples to enforce latency gate yet."));
        }
        else
        {
            checks.Add(new SloGateCheckResult(
                Gate: "task_completion.p95_ms",
                Passed: taskLatencyStats.P95 <= options.MaxTaskCompletionP95Ms,
                Status: "enforced",
                Value: taskLatencyStats.P95,
                Threshold: options.MaxTaskCompletionP95Ms,
                Unit: "ms",
                Message: $"count={taskLatencyStats.Count}, p95={taskLatencyStats.P95:F2}"));
        }

        var autonomyTaskTotal = _metrics.GetCounter("autonomy.task.in_scope_total");
        var autonomyTaskCompletionRatio = _metrics.GetGauge("autonomy_in_scope_task_completion_ratio");
        var autonomyTerminalFailureRatio = _metrics.GetGauge("autonomy_in_scope_terminal_failure_ratio");

        if (autonomyTaskTotal < options.MinAutonomyTaskSamples)
        {
            checks.Add(new SloGateCheckResult(
                Gate: "autonomy.task_completion_ratio",
                Passed: !options.FailOnInsufficientData,
                Status: "insufficient_data",
                Value: autonomyTaskTotal,
                Threshold: options.MinAutonomyTaskSamples,
                Unit: "samples",
                Message: "Not enough in-scope autonomy task samples to enforce completion ratio gate yet."));

            checks.Add(new SloGateCheckResult(
                Gate: "autonomy.terminal_failure_ratio",
                Passed: !options.FailOnInsufficientData,
                Status: "insufficient_data",
                Value: autonomyTaskTotal,
                Threshold: options.MinAutonomyTaskSamples,
                Unit: "samples",
                Message: "Not enough in-scope autonomy task samples to enforce terminal failure ratio gate yet."));
        }
        else
        {
            checks.Add(new SloGateCheckResult(
                Gate: "autonomy.task_completion_ratio",
                Passed: autonomyTaskCompletionRatio >= options.MinAutonomyTaskCompletionRatio,
                Status: "enforced",
                Value: autonomyTaskCompletionRatio,
                Threshold: options.MinAutonomyTaskCompletionRatio,
                Unit: "ratio",
                Message: $"in_scope_task_total={autonomyTaskTotal}"));

            checks.Add(new SloGateCheckResult(
                Gate: "autonomy.terminal_failure_ratio",
                Passed: autonomyTerminalFailureRatio <= options.MaxAutonomyTerminalFailureRatio,
                Status: "enforced",
                Value: autonomyTerminalFailureRatio,
                Threshold: options.MaxAutonomyTerminalFailureRatio,
                Unit: "ratio",
                Message: $"in_scope_task_total={autonomyTaskTotal}"));
        }

        var autonomyReplayTotal = _metrics.GetCounter("autonomy.replay.total");
        var autonomyReplaySuccessRatio = _metrics.GetGauge("autonomy_replay_success_ratio");

        if (autonomyReplayTotal < options.MinAutonomyReplaySamples)
        {
            checks.Add(new SloGateCheckResult(
                Gate: "autonomy.replay_success_ratio",
                Passed: !options.FailOnInsufficientData,
                Status: "insufficient_data",
                Value: autonomyReplayTotal,
                Threshold: options.MinAutonomyReplaySamples,
                Unit: "samples",
                Message: "Not enough autonomy replay samples to enforce replay success ratio gate yet."));
        }
        else
        {
            checks.Add(new SloGateCheckResult(
                Gate: "autonomy.replay_success_ratio",
                Passed: autonomyReplaySuccessRatio >= options.MinAutonomyReplaySuccessRatio,
                Status: "enforced",
                Value: autonomyReplaySuccessRatio,
                Threshold: options.MinAutonomyReplaySuccessRatio,
                Unit: "ratio",
                Message: $"replay_total={autonomyReplayTotal}"));
        }

        var autonomyTerminalStats = _metrics.GetHistogramStats("autonomy.time_to_terminal_ms");
        if (autonomyTerminalStats.Count < options.MinAutonomyTerminalSamples)
        {
            checks.Add(new SloGateCheckResult(
                Gate: "autonomy.median_time_to_terminal_ms",
                Passed: !options.FailOnInsufficientData,
                Status: "insufficient_data",
                Value: autonomyTerminalStats.Count,
                Threshold: options.MinAutonomyTerminalSamples,
                Unit: "samples",
                Message: "Not enough autonomy terminal latency samples to enforce median gate yet."));
        }
        else
        {
            checks.Add(new SloGateCheckResult(
                Gate: "autonomy.median_time_to_terminal_ms",
                Passed: autonomyTerminalStats.P50 <= options.MaxAutonomyMedianTimeToTerminalMs,
                Status: "enforced",
                Value: autonomyTerminalStats.P50,
                Threshold: options.MaxAutonomyMedianTimeToTerminalMs,
                Unit: "ms",
                Message: $"count={autonomyTerminalStats.Count}, p50={autonomyTerminalStats.P50:F2}"));
        }

        return new SloGateEvaluationResult(
            Passed: checks.All(c => c.Passed),
            EvaluatedAtUtc: DateTimeOffset.UtcNow,
            Checks: checks);
    }
}