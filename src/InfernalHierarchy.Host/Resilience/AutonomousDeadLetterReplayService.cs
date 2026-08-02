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
                foreach (var candidate in pending.OrderBy(x => x.OccurredAtUtc))
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

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
}
