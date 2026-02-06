using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Memory.Maintenance;

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
        var period = TimeSpan.FromHours(_options.PruningIntervalHours);
        if (period <= TimeSpan.Zero)
        {
            period = TimeSpan.FromHours(24);
        }

        _timer = new PeriodicTimer(period);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("🗑️ Memory pruning is disabled");
            return;
        }

        _logger.LogInformation(
            "🗑️ Memory pruning service started - every {Hours}h (retention={RetentionDays}d, minConfidence<{MinConfidence}, archival={Archival}, dryRun={DryRun}, maxDeletesPerRun={MaxDeletesPerRun})",
            _options.PruningIntervalHours,
            _options.RetentionDays,
            _options.MinConfidenceThreshold,
            _options.EnableArchival,
            _options.DryRun,
            _options.MaxDeletesPerRun);

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
        var wouldPruneCount = 0;
        var remainingBudget = _options.MaxDeletesPerRun <= 0 ? int.MaxValue : _options.MaxDeletesPerRun;
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.RetentionDays);

        // Prune old facts with low confidence
        var (factsPruned, factsWould) = await PruneOldFactsWithLowConfidenceAsync(cutoffDate, remainingBudget, ct);
        prunedCount += factsPruned;
        wouldPruneCount += factsWould;
        remainingBudget -= factsPruned;

        // Prune completed tasks
        if (remainingBudget > 0)
        {
            var (tasksPruned, tasksWould) = await PruneCompletedTasksAsync(cutoffDate, remainingBudget, ct);
            prunedCount += tasksPruned;
            wouldPruneCount += tasksWould;
            remainingBudget -= tasksPruned;
        }

        // Archive old decisions (if archival is enabled)
        if (remainingBudget > 0 && _options.EnableArchival)
        {
            var (decisionsPruned, decisionsWould) = await ArchiveOldDecisionsAsync(cutoffDate, remainingBudget, ct);
            prunedCount += decisionsPruned;
            wouldPruneCount += decisionsWould;
        }

        if (_options.DryRun)
        {
            _logger.LogInformation("✅ Memory pruning dry-run complete - would remove {Count} entries", wouldPruneCount);
        }
        else
        {
            _logger.LogInformation("✅ Memory pruning complete - removed {Count} entries", prunedCount);
        }
    }

    private async Task<(int pruned, int wouldPrune)> PruneOldFactsWithLowConfidenceAsync(DateTime cutoffDate, int budget, CancellationToken ct)
    {
        var pruned = 0;
        var wouldPrune = 0;
        try
        {
            // Search all facts
            var allFacts = await _sharedMemory.SearchFactsAsync("", ct);

            foreach (var fact in allFacts)
            {
                if (pruned >= budget)
                {
                    break;
                }

                // Prune if old AND low confidence
                if (fact.CreatedAt < cutoffDate && fact.Confidence < _options.MinConfidenceThreshold)
                {
                    _logger.LogDebug("Pruning low-confidence fact: {FactId} (confidence: {Confidence})",
                        fact.Id, fact.Confidence);

                    if (_options.DryRun)
                    {
                        wouldPrune++;
                    }
                    else
                    {
                        await _sharedMemory.DeleteFactAsync(fact.Id, ct);
                        pruned++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pruning old facts");
        }

        return (pruned, wouldPrune);
    }

    private async Task<(int pruned, int wouldPrune)> PruneCompletedTasksAsync(DateTime cutoffDate, int budget, CancellationToken ct)
    {
        var pruned = 0;
        var wouldPrune = 0;
        try
        {
            var completedTasks = await _sharedMemory.GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus.Completed, ct);

            foreach (var task in completedTasks)
            {
                if (pruned >= budget)
                {
                    break;
                }

                if (task.CompletedAt.HasValue && task.CompletedAt.Value < cutoffDate)
                {
                    _logger.LogDebug("Pruning old completed task: {TaskId}", task.Id);

                    if (_options.DryRun)
                    {
                        wouldPrune++;
                    }
                    else
                    {
                        await _sharedMemory.DeleteTaskAsync(task.Id, ct);
                        pruned++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pruning completed tasks");
        }

        return (pruned, wouldPrune);
    }

    private async Task<(int pruned, int wouldPrune)> ArchiveOldDecisionsAsync(DateTime cutoffDate, int budget, CancellationToken ct)
    {
        var pruned = 0;
        var wouldPrune = 0;
        try
        {
            var oldDecisions = await _sharedMemory.GetRecentDecisionsAsync(_options.DecisionsToScan, ct);

            foreach (var decision in oldDecisions.Where(d => d.CreatedAt < cutoffDate))
            {
                if (pruned >= budget)
                {
                    break;
                }

                _logger.LogDebug("Archiving old decision: {DecisionId}", decision.Id);

                if (_options.DryRun)
                {
                    wouldPrune++;
                    continue;
                }

                Directory.CreateDirectory(_options.ArchivePath);

                // Archive to file or external storage
                var archivePath = Path.Combine(_options.ArchivePath, $"decision_{decision.Id}.json");
                var json = System.Text.Json.JsonSerializer.Serialize(decision, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(archivePath, json, ct);

                // Then delete from active memory
                await _sharedMemory.DeleteDecisionAsync(decision.Id, ct);
                pruned++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving old decisions");
        }

        return (pruned, wouldPrune);
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }
}
