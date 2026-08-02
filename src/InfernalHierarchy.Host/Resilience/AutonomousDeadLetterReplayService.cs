namespace InfernalHierarchy.Host.Resilience;

internal sealed class AutonomousDeadLetterReplayService : BackgroundService
{
    private const string RequestedBy = "autonomous-replay-worker";

    private readonly IFailedOperationStore _store;
    private readonly DeadLetterReplayService _replay;
    private readonly FailedOperationHandlingOptions _options;
    private readonly ILogger<AutonomousDeadLetterReplayService> _logger;

    public AutonomousDeadLetterReplayService(
        IFailedOperationStore store,
        DeadLetterReplayService replay,
        IOptions<FailedOperationHandlingOptions> options,
        ILogger<AutonomousDeadLetterReplayService> logger)
    {
        _store = store;
        _replay = replay;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.AutonomousReplayEnabled)
        {
            _logger.LogInformation("Autonomous dead-letter replay worker disabled by configuration");
            return;
        }

        var batchSize = Math.Max(1, _options.ReplayBatchSize);
        var pollMs = Math.Max(50, _options.ReplayPollIntervalMs);
        var maxAttemptsPerLoop = Math.Max(1, Math.Min(batchSize, _options.ReplayMaxAttemptsPerLoop));

        _logger.LogInformation(
            "Autonomous dead-letter replay worker started (batch={BatchSize}, poll_ms={PollMs})",
            batchSize,
            pollMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await _store
                    .GetRecentAsync(batchSize, pendingOnly: true, stoppingToken)
                    .ConfigureAwait(false);

                if (pending.Count == 0)
                {
                    await Task.Delay(pollMs, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var replayedAny = false;
                var attemptedInLoop = 0;
                var nowUtc = DateTimeOffset.UtcNow;

                foreach (var candidate in pending.OrderBy(x => x.OccurredAtUtc))
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (attemptedInLoop >= maxAttemptsPerLoop)
                    {
                        break;
                    }

                    if (!IsReplayDue(candidate, nowUtc))
                    {
                        continue;
                    }

                    attemptedInLoop++;

                    var result = await _replay.ReplayAsync(candidate.Id, RequestedBy, stoppingToken).ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        replayedAny = true;
                        continue;
                    }

                    if (result.Available)
                    {
                        _logger.LogDebug(
                            "Autonomous replay failed for {DeadLetterId} (reason={ReasonCode}, error={Error})",
                            candidate.Id,
                            result.ReasonCode ?? string.Empty,
                            result.Error ?? string.Empty);
                    }
                }

                if (!replayedAny)
                {
                    await Task.Delay(pollMs, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Autonomous dead-letter replay loop error");
                await Task.Delay(pollMs, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Autonomous dead-letter replay worker stopped");
    }

    private bool IsReplayDue(FailedOperationRecord record, DateTimeOffset nowUtc)
    {
        if (record.ReplayAttempts <= 0 || record.LastReplayAttemptAtUtc is null)
        {
            return true;
        }

        var initialBackoffMs = Math.Max(1, _options.ReplayInitialBackoffMs);
        var maxBackoffMs = Math.Max(initialBackoffMs, _options.ReplayMaxBackoffMs);

        var exponent = Math.Max(0, record.ReplayAttempts - 1);
        var delayMs = initialBackoffMs * Math.Pow(2, exponent);
        delayMs = Math.Min(delayMs, maxBackoffMs);

        var jitterRatio = Math.Clamp(_options.ReplayJitterRatio, 0d, 1d);
        var jitterBoundMs = delayMs * jitterRatio;
        var jitterOffsetMs = ComputeDeterministicJitterMs(record.Id, jitterBoundMs);

        var dueAt = record.LastReplayAttemptAtUtc.Value
            .AddMilliseconds(delayMs + jitterOffsetMs);

        return nowUtc >= dueAt;
    }

    private static double ComputeDeterministicJitterMs(string id, double jitterBoundMs)
    {
        if (jitterBoundMs <= 0 || string.IsNullOrWhiteSpace(id))
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var ch in id)
            {
                hash = (hash * 31) + ch;
            }

            // Scale hash deterministically to [-1, 1] then to jitter range.
            var normalized = ((hash & 0x7fffffff) / (double)int.MaxValue) * 2d - 1d;
            return normalized * jitterBoundMs;
        }
    }
}
