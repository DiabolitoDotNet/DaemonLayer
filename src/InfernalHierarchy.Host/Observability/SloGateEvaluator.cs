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
                Passed: true,
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
                Passed: true,
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
                Passed: true,
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

        return new SloGateEvaluationResult(
            Passed: checks.All(c => c.Passed),
            EvaluatedAtUtc: DateTimeOffset.UtcNow,
            Checks: checks);
    }
}