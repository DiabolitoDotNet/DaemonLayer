using Microsoft.Extensions.Hosting;
using InfernalHierarchy.Memory.Storage;

namespace InfernalHierarchy.Memory.Maintenance;

public sealed class MemoryCompactionService : BackgroundService
{
    private readonly LiteDbSharedMemory _sharedMemory;
    private readonly MemoryCompactionOptions _options;
    private readonly ILogger<MemoryCompactionService> _logger;

    public MemoryCompactionService(
        LiteDbSharedMemory sharedMemory,
        IOptions<MemoryCompactionOptions> options,
        ILogger<MemoryCompactionService> logger)
    {
        _sharedMemory = sharedMemory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("🧱 LiteDB compaction service is disabled");
            return;
        }

        _logger.LogInformation(
            "🧱 LiteDB compaction service started - every {Hours}h (startup={Startup}, minSizeBytes={MinSize}, includeErrorReport={IncludeErrorReport})",
            _options.IntervalHours,
            _options.RunOnStartup,
            _options.MinDatabaseSizeBytes,
            _options.IncludeErrorReport);

        if (_options.RunOnStartup)
        {
            await RunCompactionIfNeededAsync(stoppingToken).ConfigureAwait(false);
        }

        var interval = TimeSpan.FromHours(_options.IntervalHours);
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunCompactionIfNeededAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("LiteDB compaction service stopping due to cancellation");
        }
    }

    private async Task RunCompactionIfNeededAsync(CancellationToken ct)
    {
        try
        {
            var dbPath = _sharedMemory.DatabasePath;
            var fileInfo = new FileInfo(dbPath);

            if (!fileInfo.Exists)
            {
                _logger.LogDebug("LiteDB compaction skipped: database file does not exist yet ({Path})", dbPath);
                return;
            }

            var currentSizeBytes = fileInfo.Length;
            if (currentSizeBytes < _options.MinDatabaseSizeBytes)
            {
                _logger.LogDebug(
                    "LiteDB compaction skipped: current size {CurrentSize}B is below threshold {Threshold}B",
                    currentSizeBytes,
                    _options.MinDatabaseSizeBytes);
                return;
            }

            var result = await _sharedMemory.RebuildAsync(_options.IncludeErrorReport, ct).ConfigureAwait(false);
            var reclaimedBytes = Math.Max(0, result.BeforeBytes - result.AfterBytes);

            _logger.LogInformation(
                "🧱 LiteDB compaction finished (before={BeforeBytes}B, after={AfterBytes}B, reclaimed={ReclaimedBytes}B)",
                result.BeforeBytes,
                result.AfterBytes,
                reclaimedBytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LiteDB compaction run failed");
        }
    }
}