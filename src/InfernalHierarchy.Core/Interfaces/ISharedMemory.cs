
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Shared memory abstraction.
/// Implementations persist durable agent context such as decisions, facts, and tasks.
/// The primary implementation is LiteDB-backed, but callers should rely on this interface.
/// </summary>
public interface ISharedMemory
{
    /// <summary>
    /// Stores a decision record.
    /// </summary>
    Task AddDecisionAsync(Decision decision, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a decision by id.
    /// </summary>
    Task<Decision?> GetDecisionAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recent decisions.
    /// </summary>
    Task<IEnumerable<Decision>> GetRecentDecisionsAsync(int count = 10, CancellationToken ct = default);

    /// <summary>
    /// Searches decisions using an implementation-defined strategy (keyword, full-text, etc.).
    /// </summary>
    Task<IEnumerable<Decision>> SearchDecisionsAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Deletes a decision by id.
    /// </summary>
    Task DeleteDecisionAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Stores a fact record.
    /// </summary>
    Task AddFactAsync(Fact fact, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a fact by id.
    /// </summary>
    Task<Fact?> GetFactAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all facts in a category.
    /// </summary>
    Task<IEnumerable<Fact>> GetFactsByCategoryAsync(string category, CancellationToken ct = default);

    /// <summary>
    /// Searches facts using an implementation-defined strategy (keyword, full-text, etc.).
    /// </summary>
    Task<IEnumerable<Fact>> SearchFactsAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Updates a fact and records the change reason/version history.
    /// </summary>
    Task UpdateFactAsync(Fact fact, string changeReason, CancellationToken ct = default);

    /// <summary>
    /// Gets the historical versions for a fact.
    /// </summary>
    Task<IEnumerable<FactVersion>> GetFactHistoryAsync(string factId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a fact by id.
    /// </summary>
    Task DeleteFactAsync(string id, CancellationToken ct = default);
    
    /// <summary>
    /// Retrieves facts visible to the requesting agent based on rank and sharing rules.
    /// Implementations should enforce <see cref="MemoryVisibility"/>, <c>SharedWithAgents</c>, and minimum-rank semantics.
    /// </summary>
    Task<IEnumerable<Fact>> GetVisibleFactsAsync(string requestingAgentId, AgentRank requestingAgentRank, CancellationToken ct = default);

    /// <summary>
    /// Searches visible facts for the requesting agent.
    /// </summary>
    Task<IEnumerable<Fact>> SearchVisibleFactsAsync(string query, string requestingAgentId, AgentRank requestingAgentRank, CancellationToken ct = default);

    /// <summary>
    /// Stores a task entry.
    /// </summary>
    Task AddTaskAsync(TaskEntry task, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a task by id.
    /// </summary>
    Task<TaskEntry?> GetTaskAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing task entry.
    /// </summary>
    Task UpdateTaskAsync(TaskEntry task, CancellationToken ct = default);

    /// <summary>
    /// Lists tasks by status.
    /// </summary>
    Task<IEnumerable<TaskEntry>> GetTasksByStatusAsync(Entities.TaskStatus status, CancellationToken ct = default);

    /// <summary>
    /// Lists tasks assigned to an agent.
    /// </summary>
    Task<IEnumerable<TaskEntry>> GetTasksByAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a task by id.
    /// </summary>
    Task DeleteTaskAsync(string id, CancellationToken ct = default);
}
