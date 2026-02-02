using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Memory;

public class MemoryOptions
{
    public string DatabasePath { get; set; } = "data/infernal.db";
}

/// <summary>
/// LiteDB implementation of shared memory
/// </summary>
public sealed class LiteDbSharedMemory : ISharedMemory, IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILogger<LiteDbSharedMemory> _logger;

    private ILiteCollection<Decision> Decisions => _db.GetCollection<Decision>("decisions");
    private ILiteCollection<Fact> Facts => _db.GetCollection<Fact>("facts");
    private ILiteCollection<TaskEntry> Tasks => _db.GetCollection<TaskEntry>("tasks");

    public LiteDbSharedMemory(IOptions<MemoryOptions> options, ILogger<LiteDbSharedMemory> logger)
    {
        _logger = logger;

        var dbPath = options.Value.DatabasePath;
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _db = new LiteDatabase(dbPath);

        // Create indexes for better query performance
        Decisions.EnsureIndex(x => x.CreatedAt);
        Decisions.EnsureIndex(x => x.CreatedBy);

        Facts.EnsureIndex(x => x.Category);
        Facts.EnsureIndex(x => x.CreatedAt);

        Tasks.EnsureIndex(x => x.Status);
        Tasks.EnsureIndex(x => x.AssignedTo);
        Tasks.EnsureIndex(x => x.CreatedAt);

        _logger.LogInformation("💾 LiteDB shared memory initialized at {Path}", dbPath);
    }

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
        return Task.FromResult(decision);
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
        return Task.FromResult(fact);
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
        return Task.FromResult(task);
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

    #endregion

    public void Dispose()
    {
        _db?.Dispose();
    }
}
