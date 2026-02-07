
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
    Task DeleteDecisionAsync(string id, CancellationToken ct = default);

    // Facts
    Task AddFactAsync(Fact fact, CancellationToken ct = default);
    Task<Fact?> GetFactAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<Fact>> GetFactsByCategoryAsync(string category, CancellationToken ct = default);
    Task<IEnumerable<Fact>> SearchFactsAsync(string query, CancellationToken ct = default);
    Task UpdateFactAsync(Fact fact, string changeReason, CancellationToken ct = default);
    Task<IEnumerable<FactVersion>> GetFactHistoryAsync(string factId, CancellationToken ct = default);
    Task DeleteFactAsync(string id, CancellationToken ct = default);
    
    // Visibility-aware facts (filters based on agent rank and sharing rules)
    Task<IEnumerable<Fact>> GetVisibleFactsAsync(string requestingAgentId, AgentRank requestingAgentRank, CancellationToken ct = default);
    Task<IEnumerable<Fact>> SearchVisibleFactsAsync(string query, string requestingAgentId, AgentRank requestingAgentRank, CancellationToken ct = default);

    // Tasks
    Task AddTaskAsync(TaskEntry task, CancellationToken ct = default);
    Task<TaskEntry?> GetTaskAsync(string id, CancellationToken ct = default);
    Task UpdateTaskAsync(TaskEntry task, CancellationToken ct = default);
    Task<IEnumerable<TaskEntry>> GetTasksByStatusAsync(Entities.TaskStatus status, CancellationToken ct = default);
    Task<IEnumerable<TaskEntry>> GetTasksByAgentAsync(string agentId, CancellationToken ct = default);
    Task DeleteTaskAsync(string id, CancellationToken ct = default);
}
