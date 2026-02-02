using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Memory;

/// <summary>
/// Background service for automatic memory pruning and archival
/// </summary>
public class MemoryPruningService : BackgroundService
{
    private readonly ISharedMemory _sharedMemory;
    private readonly ILogger<MemoryPruningService> _logger;
    private readonly MemoryPruningOptions _options;
    private readonly PeriodicTimer _timer;

    public MemoryPruningService(
        ISharedMemory sharedMemory,
        IOptions<MemoryPruningOptions> options,
        ILogger<MemoryPruningService> logger)
    {
        _sharedMemory = sharedMemory;
        _options = options.Value;
        _logger = logger;
        _timer = new PeriodicTimer(TimeSpan.FromHours(_options.PruningIntervalHours));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("🗑️ Memory pruning is disabled");
            return;
        }

        _logger.LogInformation("🗑️ Memory pruning service started - running every {Hours}h", _options.PruningIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _timer.WaitForNextTickAsync(stoppingToken);
                await PruneMemoryAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during memory pruning");
            }
        }
    }

    private async Task PruneMemoryAsync(CancellationToken ct)
    {
        _logger.LogInformation("🧹 Starting memory pruning...");

        var prunedCount = 0;
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.RetentionDays);

        // Prune old facts with low confidence
        await PruneOldFactsWithLowConfidenceAsync(cutoffDate, ct);

        // Prune completed tasks
        await PruneCompletedTasksAsync(cutoffDate, ct);

        // Archive old decisions (if archival is enabled)
        if (_options.EnableArchival)
        {
            await ArchiveOldDecisionsAsync(cutoffDate, ct);
        }

        _logger.LogInformation("✅ Memory pruning complete - removed {Count} entries", prunedCount);
    }

    private async Task PruneOldFactsWithLowConfidenceAsync(DateTime cutoffDate, CancellationToken ct)
    {
        try
        {
            // Search all facts
            var allFacts = await _sharedMemory.SearchFactsAsync("", ct);

            foreach (var fact in allFacts)
            {
                // Prune if old AND low confidence
                if (fact.CreatedAt < cutoffDate && fact.Confidence < _options.MinConfidenceThreshold)
                {
                    _logger.LogDebug("Pruning low-confidence fact: {FactId} (confidence: {Confidence})",
                        fact.Id, fact.Confidence);

                    // Note: ISharedMemory doesn't have Delete methods yet
                    // This would need to be added to the interface
                    // await _sharedMemory.DeleteFactAsync(fact.Id, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pruning old facts");
        }
    }

    private async Task PruneCompletedTasksAsync(DateTime cutoffDate, CancellationToken ct)
    {
        try
        {
            var completedTasks = await _sharedMemory.GetTasksByStatusAsync(Core.Entities.TaskStatus.Completed, ct);

            foreach (var task in completedTasks)
            {
                if (task.CompletedAt.HasValue && task.CompletedAt.Value < cutoffDate)
                {
                    _logger.LogDebug("Pruning old completed task: {TaskId}", task.Id);
                    // await _sharedMemory.DeleteTaskAsync(task.Id, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pruning completed tasks");
        }
    }

    private async Task ArchiveOldDecisionsAsync(DateTime cutoffDate, CancellationToken ct)
    {
        try
        {
            var oldDecisions = await _sharedMemory.GetRecentDecisionsAsync(1000, ct);

            foreach (var decision in oldDecisions.Where(d => d.CreatedAt < cutoffDate))
            {
                _logger.LogDebug("Archiving old decision: {DecisionId}", decision.Id);

                // Archive to file or external storage
                var archivePath = Path.Combine(_options.ArchivePath, $"decision_{decision.Id}.json");
                var json = System.Text.Json.JsonSerializer.Serialize(decision, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(archivePath, json, ct);

                // Then delete from active memory
                // await _sharedMemory.DeleteDecisionAsync(decision.Id, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving old decisions");
        }
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }
}

public class MemoryPruningOptions
{
    public bool Enabled { get; set; } = false;
    public int PruningIntervalHours { get; set; } = 24;
    public int RetentionDays { get; set; } = 30;
    public double MinConfidenceThreshold { get; set; } = 0.3;
    public bool EnableArchival { get; set; } = false;
    public string ArchivePath { get; set; } = "./archive/memory";
}
