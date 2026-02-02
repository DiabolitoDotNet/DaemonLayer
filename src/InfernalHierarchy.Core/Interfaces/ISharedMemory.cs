using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Shared memory repository using LiteDB
/// </summary>
public interface ISharedMemory
{
    // Decisions
    Task AddDecisionAsync(Decision decision, CancellationToken ct = default);
    Task<Decision?> GetDecisionAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<Decision>> GetRecentDecisionsAsync(int count = 10, CancellationToken ct = default);
    Task<IEnumerable<Decision>> SearchDecisionsAsync(string query, CancellationToken ct = default);

    // Facts
    Task AddFactAsync(Fact fact, CancellationToken ct = default);
    Task<Fact?> GetFactAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<Fact>> GetFactsByCategoryAsync(string category, CancellationToken ct = default);
    Task<IEnumerable<Fact>> SearchFactsAsync(string query, CancellationToken ct = default);

    // Tasks
    Task AddTaskAsync(TaskEntry task, CancellationToken ct = default);
    Task<TaskEntry?> GetTaskAsync(string id, CancellationToken ct = default);
    Task UpdateTaskAsync(TaskEntry task, CancellationToken ct = default);
    Task<IEnumerable<TaskEntry>> GetTasksByStatusAsync(Entities.TaskStatus status, CancellationToken ct = default);
    Task<IEnumerable<TaskEntry>> GetTasksByAgentAsync(string agentId, CancellationToken ct = default);
}
