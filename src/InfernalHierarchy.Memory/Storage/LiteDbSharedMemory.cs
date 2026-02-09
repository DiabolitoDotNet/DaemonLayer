using LiteDB;

namespace InfernalHierarchy.Memory.Storage;

/// <summary>
/// LiteDB implementation of shared memory
/// </summary>
public sealed class LiteDbSharedMemory : ISharedMemory, IToolResultCacheStore, ICustomToolStore, IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILogger<LiteDbSharedMemory> _logger;

    private ILiteCollection<Decision> Decisions => _db.GetCollection<Decision>("decisions");
    private ILiteCollection<Fact> Facts => _db.GetCollection<Fact>("facts");
    private ILiteCollection<TaskEntry> Tasks => _db.GetCollection<TaskEntry>("tasks");
    private ILiteCollection<CachedToolResult> ToolCache => _db.GetCollection<CachedToolResult>("tool_cache");
    private ILiteCollection<CustomToolDefinition> CustomTools => _db.GetCollection<CustomToolDefinition>("custom_tools");

    public LiteDbSharedMemory(IOptions<MemoryOptions> options, ILogger<LiteDbSharedMemory> logger)
    {
        _logger = logger;

        var dbPath = options.Value.DatabasePath;
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var mapper = new BsonMapper();
        mapper.Entity<MemoryEntry>().Id(x => x.Id);
        mapper.Entity<CachedToolResult>().Id(x => x.InputKey);
        mapper.Entity<CustomToolDefinition>().Id(x => x.Id);

        _db = new LiteDatabase(dbPath, mapper);

        // Create indexes for better query performance
        Decisions.EnsureIndex(nameof(MemoryEntry.CreatedAt));
        Decisions.EnsureIndex(nameof(MemoryEntry.CreatedBy));

        Facts.EnsureIndex(nameof(Fact.Category));
        Facts.EnsureIndex(nameof(MemoryEntry.CreatedAt));

        Tasks.EnsureIndex(nameof(TaskEntry.Status));
        Tasks.EnsureIndex(nameof(TaskEntry.AssignedTo));
        Tasks.EnsureIndex(nameof(MemoryEntry.CreatedAt));

        ToolCache.EnsureIndex(nameof(CachedToolResult.ToolName));
        ToolCache.EnsureIndex(nameof(CachedToolResult.ExpiresAt));

        CustomTools.EnsureIndex(nameof(CustomToolDefinition.ToolName));
        CustomTools.EnsureIndex(nameof(CustomToolDefinition.CreatedAt));

        _logger.LogInformation("💾 LiteDB shared memory initialized at {Path}", dbPath);
    }

    #region Custom Tools

    public Task UpsertAsync(CustomToolDefinition tool, CancellationToken ct = default)
    {
        if (tool is null)
        {
            throw new ArgumentNullException(nameof(tool));
        }

        if (string.IsNullOrWhiteSpace(tool.Id))
        {
            tool.Id = Guid.NewGuid().ToString("n");
        }

        CustomTools.Upsert(tool);
        return Task.CompletedTask;
    }

    public Task<CustomToolDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<CustomToolDefinition?>(null);
        }

        var tool = CustomTools.FindById(id);
        return Task.FromResult<CustomToolDefinition?>(tool);
    }

    public Task<CustomToolDefinition?> GetByNameAsync(string toolName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return Task.FromResult<CustomToolDefinition?>(null);
        }

        var normalized = toolName.Trim();
        var tool = CustomTools.FindOne(x => x.ToolName == normalized);
        return Task.FromResult<CustomToolDefinition?>(tool);
    }

    public Task<IReadOnlyList<CustomToolDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        var tools = CustomTools
            .Query()
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<CustomToolDefinition>>(tools);
    }

    #endregion

    #region Tool Result Cache

    public Task<CachedToolResult?> GetAsync(string inputKey, CancellationToken ct = default)
    {
        var cached = ToolCache.FindById(inputKey);
        if (cached is null)
        {
            return Task.FromResult<CachedToolResult?>(null);
        }

        if (cached.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            ToolCache.Delete(inputKey);
            return Task.FromResult<CachedToolResult?>(null);
        }

        return Task.FromResult<CachedToolResult?>(cached);
    }

    public Task UpsertAsync(CachedToolResult entry, CancellationToken ct = default)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        ToolCache.Upsert(entry);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string inputKey, CancellationToken ct = default)
    {
        var removed = ToolCache.Delete(inputKey);
        return Task.FromResult(removed);
    }

    public Task<int> PruneExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var removed = ToolCache.DeleteMany(x => x.ExpiresAt <= now);
        return Task.FromResult(removed);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        ToolCache.DeleteAll();
        return Task.CompletedTask;
    }

    #endregion

    #region Decisions

    public Task AddDecisionAsync(Decision decision, CancellationToken ct = default)
    {
        Decisions.Insert(decision);
        _logger.LogDebug("Decision added: {Action} by {Agent}", decision.Action, decision.CreatedBy);
        return Task.CompletedTask;
    }

    public Task<Decision?> GetDecisionAsync(string id, CancellationToken ct = default)
    {
        var decision = Decisions.FindById(id);
        return Task.FromResult<Decision?>(decision);
    }

    public Task<IEnumerable<Decision>> GetRecentDecisionsAsync(int count = 10, CancellationToken ct = default)
    {
        var decisions = Decisions
            .Query()
            .OrderByDescending(x => x.CreatedAt)
            .Limit(count)
            .ToList();

        return Task.FromResult<IEnumerable<Decision>>(decisions);
    }

    public Task<IEnumerable<Decision>> SearchDecisionsAsync(string query, CancellationToken ct = default)
    {
        var decisions = Decisions
            .Query()
            .Where(x => x.Context.Contains(query) || x.Action.Contains(query) || x.Reasoning.Contains(query))
            .ToList();

        return Task.FromResult<IEnumerable<Decision>>(decisions);
    }

    public Task DeleteDecisionAsync(string id, CancellationToken ct = default)
    {
        var deleted = Decisions.Delete(id);
        if (deleted)
        {
            _logger.LogInformation("Decision deleted: {Id}", id);
        }
        else
        {
            _logger.LogWarning("Decision not found for deletion: {Id}", id);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Facts

    public Task AddFactAsync(Fact fact, CancellationToken ct = default)
    {
        Facts.Insert(fact);
        _logger.LogDebug("Fact added: {Category} - {Content}", fact.Category, fact.Content);
        return Task.CompletedTask;
    }

    public Task<Fact?> GetFactAsync(string id, CancellationToken ct = default)
    {
        var fact = Facts.FindById(id);
        return Task.FromResult<Fact?>(fact);
    }

    public Task<IEnumerable<Fact>> GetFactsByCategoryAsync(string category, CancellationToken ct = default)
    {
        var facts = Facts
            .Query()
            .Where(x => x.Category == category)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Task.FromResult<IEnumerable<Fact>>(facts);
    }

    public Task<IEnumerable<Fact>> SearchFactsAsync(string query, CancellationToken ct = default)
    {
        var facts = Facts
            .Query()
            .Where(x => x.Content.Contains(query) || x.Category.Contains(query))
            .ToList();

        return Task.FromResult<IEnumerable<Fact>>(facts);
    }

    public Task UpdateFactAsync(Fact fact, string changeReason, CancellationToken ct = default)
    {
        var existing = Facts.FindById(fact.Id);
        if (existing is not null)
        {
            // Create version history entry
            var version = new FactVersion
            {
                VersionNumber = existing.Version,
                Content = existing.Content,
                Confidence = existing.Confidence,
                ModifiedAt = existing.LastModifiedAt,
                ModifiedBy = existing.LastModifiedBy,
                ChangeReason = changeReason
            };

            fact.VersionHistory.Insert(0, version);
            fact.Version = existing.Version + 1;
            fact.LastModifiedAt = DateTime.UtcNow;

            Facts.Update(fact);
            _logger.LogInformation("Fact updated: {Id} (v{Version}) - {Reason}", fact.Id, fact.Version, changeReason);
        }
        else
        {
            _logger.LogWarning("Fact not found for update: {Id}", fact.Id);
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<FactVersion>> GetFactHistoryAsync(string factId, CancellationToken ct = default)
    {
        var fact = Facts.FindById(factId);
        if (fact is not null)
        {
            return Task.FromResult<IEnumerable<FactVersion>>(fact.VersionHistory);
        }

        return Task.FromResult<IEnumerable<FactVersion>>(Array.Empty<FactVersion>());
    }

    public Task DeleteFactAsync(string id, CancellationToken ct = default)
    {
        var deleted = Facts.Delete(id);
        if (deleted)
        {
            _logger.LogInformation("Fact deleted: {Id}", id);
        }
        else
        {
            _logger.LogWarning("Fact not found for deletion: {Id}", id);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Tasks

    public Task AddTaskAsync(TaskEntry task, CancellationToken ct = default)
    {
        Tasks.Insert(task);
        _logger.LogDebug("Task added: {Description} assigned to {Agent}", task.Description, task.AssignedTo);
        return Task.CompletedTask;
    }

    public Task<TaskEntry?> GetTaskAsync(string id, CancellationToken ct = default)
    {
        var task = Tasks.FindById(id);
        return Task.FromResult<TaskEntry?>(task);
    }

    public Task UpdateTaskAsync(TaskEntry task, CancellationToken ct = default)
    {
        Tasks.Update(task);
        _logger.LogDebug("Task updated: {Id} - Status: {Status}", task.Id, task.Status);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<TaskEntry>> GetTasksByStatusAsync(Core.Entities.TaskStatus status, CancellationToken ct = default)
    {
        var tasks = Tasks
            .Query()
            .Where(x => x.Status == status)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Task.FromResult<IEnumerable<TaskEntry>>(tasks);
    }

    public Task<IEnumerable<TaskEntry>> GetTasksByAgentAsync(string agentId, CancellationToken ct = default)
    {
        var tasks = Tasks
            .Query()
            .Where(x => x.AssignedTo == agentId)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Task.FromResult<IEnumerable<TaskEntry>>(tasks);
    }

    public Task DeleteTaskAsync(string id, CancellationToken ct = default)
    {
        var deleted = Tasks.Delete(id);
        if (deleted)
        {
            _logger.LogInformation("Task deleted: {Id}", id);
        }
        else
        {
            _logger.LogWarning("Task not found for deletion: {Id}", id);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Visibility-Aware Memory Sharing

    public Task<IEnumerable<Fact>> GetVisibleFactsAsync(string requestingAgentId, AgentRank requestingAgentRank, CancellationToken ct = default)
    {
        var allFacts = Facts.FindAll().ToList();
        var visibleFacts = allFacts.Where(fact => IsFactVisibleToAgent(fact, requestingAgentId, requestingAgentRank)).ToList();

        _logger.LogDebug("Found {Count} visible facts for agent {AgentId} (rank: {Rank})",
            visibleFacts.Count, requestingAgentId, requestingAgentRank);

        return Task.FromResult<IEnumerable<Fact>>(visibleFacts);
    }

    public Task<IEnumerable<Fact>> SearchVisibleFactsAsync(
        string query,
        string requestingAgentId,
        AgentRank requestingAgentRank,
        CancellationToken ct = default)
    {
        var matchingFacts = Facts
            .Query()
            .Where(x => x.Content.Contains(query) || x.Category.Contains(query))
            .ToList();

        var visibleFacts = matchingFacts
            .Where(fact => IsFactVisibleToAgent(fact, requestingAgentId, requestingAgentRank))
            .ToList();

        _logger.LogDebug("Found {Count} visible facts matching '{Query}' for agent {AgentId}",
            visibleFacts.Count, query, requestingAgentId);

        return Task.FromResult<IEnumerable<Fact>>(visibleFacts);
    }

    private bool IsFactVisibleToAgent(Fact fact, string requestingAgentId, AgentRank requestingAgentRank)
    {
        // Creator always has access
        if (fact.CreatedBy == requestingAgentId)
        {
            return true;
        }

        switch (fact.Visibility)
        {
            case MemoryVisibility.Public:
                return true;

            case MemoryVisibility.Private:
                return false;

            case MemoryVisibility.Shared:
                return fact.SharedWithAgents.Contains(requestingAgentId);

            case MemoryVisibility.RankBased:
                if (fact.MinimumRankToView.HasValue)
                {
                    // Higher ranks have access (Supreme > Prince > Duke > Worker)
                    return requestingAgentRank <= fact.MinimumRankToView.Value;
                }
                return false;

            default:
                return false;
        }
    }

    #endregion

    public void Dispose()
    {
        _db?.Dispose();
    }
}
