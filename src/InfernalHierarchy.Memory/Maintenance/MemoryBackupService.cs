using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Storage;

namespace InfernalHierarchy.Memory.Maintenance;

public sealed class MemoryBackupService : BackgroundService
{
    private readonly LiteDbSharedMemory _sharedMemory;
    private readonly MemoryBackupOptions _options;
    private readonly ILogger<MemoryBackupService> _logger;

    public MemoryBackupService(
        LiteDbSharedMemory sharedMemory,
        IOptions<MemoryBackupOptions> options,
        ILogger<MemoryBackupService> logger)
    {
        _sharedMemory = sharedMemory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("💽 Memory backup service is disabled");
            return;
        }

        var interval = TimeSpan.FromHours(_options.IntervalHours);
        _logger.LogInformation(
            "💽 Memory backup service started - every {Hours}h (startup={Startup}, maxFiles={MaxFiles}, maxAgeDays={MaxAgeDays})",
            _options.IntervalHours,
            _options.BackupOnStartup,
            _options.MaxBackupFiles,
            _options.MaxBackupAgeDays);

        if (_options.BackupOnStartup)
        {
            await RunBackupAsync(stoppingToken).ConfigureAwait(false);
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunBackupAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunBackupAsync(CancellationToken ct)
    {
        try
        {
            var backupDirectory = ResolveBackupDirectory(_options.DirectoryPath);
            Directory.CreateDirectory(backupDirectory);

            var backupFileName = $"infernal-memory-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db";
            var backupPath = Path.Combine(backupDirectory, backupFileName);

            await _sharedMemory.CreateBackupAsync(backupPath, ct).ConfigureAwait(false);
            var deleted = PruneBackupFiles(backupDirectory, _options.MaxBackupFiles, _options.MaxBackupAgeDays);

            _logger.LogInformation(
                "💽 Memory backup created at {BackupPath} (pruned={Pruned})",
                backupPath,
                deleted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory backup run failed");
        }
    }

    private string ResolveBackupDirectory(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var dbDirectory = Path.GetDirectoryName(_sharedMemory.DatabasePath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(dbDirectory, configuredPath));
    }

    private static int PruneBackupFiles(string backupDirectory, int maxBackupFiles, int maxBackupAgeDays)
    {
        var deleted = 0;
        var files = new DirectoryInfo(backupDirectory)
            .GetFiles("infernal-memory-*.db")
            .OrderByDescending(file => file.CreationTimeUtc)
            .ToList();

        var cutoff = DateTime.UtcNow.AddDays(-maxBackupAgeDays);

        foreach (var file in files.Where(file => file.CreationTimeUtc < cutoff))
        {
            file.Delete();
            deleted++;
        }

        files = new DirectoryInfo(backupDirectory)
            .GetFiles("infernal-memory-*.db")
            .OrderByDescending(file => file.CreationTimeUtc)
            .ToList();

        foreach (var file in files.Skip(maxBackupFiles))
        {
            file.Delete();
            deleted++;
        }

        return deleted;
    }
}