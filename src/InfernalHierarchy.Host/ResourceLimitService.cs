namespace InfernalHierarchy.Host;

/// <summary>
/// Configuration for resource limits
/// </summary>
public class ResourceLimits
{
    public int MaxSupremeAgents { get; set; } = 1;
    public int MaxPrinceAgents { get; set; } = 5;
    public int MaxDukeAgents { get; set; } = 20;
    public int MaxWorkerAgents { get; set; } = 50;
    public int MaxTotalAgents { get; set; } = 100;
    public int MaxAgentDepth { get; set; } = 4;

    public int MaxMemoryDecisions { get; set; } = 10000;
    public int MaxMemoryFacts { get; set; } = 50000;
    public int MaxMemoryTasks { get; set; } = 10000;

    public int MaxMessageQueueSize { get; set; } = 1000;
    public int MaxConcurrentToolExecutions { get; set; } = 10;

    public long MaxDatabaseSizeBytes { get; set; } = 1_000_000_000; // 1GB
    public int MaxToolExecutionTimeSeconds { get; set; } = 300; // 5 minutes
    public int MaxLlmCallTimeSeconds { get; set; } = 60;
}

/// <summary>
/// Service to enforce resource limits
/// </summary>
public class ResourceLimitService
{
    private readonly ResourceLimits _limits;
    private readonly SemaphoreSlim _toolExecutionSemaphore;

    public ResourceLimitService(ResourceLimits limits)
    {
        _limits = limits;
        _toolExecutionSemaphore = new SemaphoreSlim(_limits.MaxConcurrentToolExecutions);
    }

    public bool CanCreateAgent(Core.Entities.AgentRank rank, int currentCount, int totalAgents)
    {
        // Check total agent limit
        if (totalAgents >= _limits.MaxTotalAgents)
            return false;

        // Check rank-specific limits
        return rank switch
        {
            Core.Entities.AgentRank.Supreme => currentCount < _limits.MaxSupremeAgents,
            Core.Entities.AgentRank.Prince => currentCount < _limits.MaxPrinceAgents,
            Core.Entities.AgentRank.Duke => currentCount < _limits.MaxDukeAgents,
            Core.Entities.AgentRank.Worker => currentCount < _limits.MaxWorkerAgents,
            _ => false
        };
    }

    public bool CanAddMemoryEntry(string entryType, int currentCount)
    {
        return entryType.ToLower() switch
        {
            "decision" => currentCount < _limits.MaxMemoryDecisions,
            "fact" => currentCount < _limits.MaxMemoryFacts,
            "task" => currentCount < _limits.MaxMemoryTasks,
            _ => true
        };
    }

    public bool IsDatabaseSizeAcceptable(long currentSizeBytes)
    {
        return currentSizeBytes < _limits.MaxDatabaseSizeBytes;
    }

    public async Task<T> ExecuteToolWithLimitAsync<T>(Func<Task<T>> toolFunc, CancellationToken ct = default)
    {
        await _toolExecutionSemaphore.WaitAsync(ct);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_limits.MaxToolExecutionTimeSeconds));

            return await toolFunc();
        }
        finally
        {
            _toolExecutionSemaphore.Release();
        }
    }

    public ResourceLimitStatus GetStatus()
    {
        return new ResourceLimitStatus
        {
            MaxTotalAgents = _limits.MaxTotalAgents,
            MaxSupremeAgents = _limits.MaxSupremeAgents,
            MaxPrinceAgents = _limits.MaxPrinceAgents,
            MaxDukeAgents = _limits.MaxDukeAgents,
            MaxWorkerAgents = _limits.MaxWorkerAgents,
            MaxConcurrentToolExecutions = _limits.MaxConcurrentToolExecutions,
            AvailableToolExecutionSlots = _toolExecutionSemaphore.CurrentCount,
            MaxDatabaseSizeMB = _limits.MaxDatabaseSizeBytes / (1024 * 1024)
        };
    }
}

public class ResourceLimitStatus
{
    public int MaxTotalAgents { get; set; }
    public int MaxSupremeAgents { get; set; }
    public int MaxPrinceAgents { get; set; }
    public int MaxDukeAgents { get; set; }
    public int MaxWorkerAgents { get; set; }
    public int MaxConcurrentToolExecutions { get; set; }
    public int AvailableToolExecutionSlots { get; set; }
    public long MaxDatabaseSizeMB { get; set; }
}
