using System.Text.Json;
using CoreTaskStatus = InfernalHierarchy.Core.Entities.TaskStatus;

namespace InfernalHierarchy.Tools.Tools.Agent;

/// <summary>
/// Tool to inspect the current state of the agent registry (counts + per-agent status),
/// optionally enriching with a best-effort view of what the agent is working on based on shared memory tasks.
/// </summary>
public sealed class GetAgentStatusTool : ITool
{
    private readonly ILogger<GetAgentStatusTool> _logger;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ISharedMemory _sharedMemory;

    public GetAgentStatusTool(
        ILogger<GetAgentStatusTool> logger,
        IAgentRegistry agentRegistry,
        ISharedMemory sharedMemory)
    {
        _logger = logger;
        _agentRegistry = agentRegistry;
        _sharedMemory = sharedMemory;
    }

    public string Name => "get_agent_status";

    public string Description => "Get current agent status summary (counts + per-agent status). Output is JSON including Occupied/Idle and best-effort current task from shared memory.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        try
        {
            var agents = _agentRegistry.GetAllAgents().ToList();

            var enriched = await Task.WhenAll(agents.Select(a => EnrichAsync(a, ct))).ConfigureAwait(false);

            var total = agents.Count;
            var idleCount = agents.Count(a => a.Status == AgentStatus.Idle);
            var occupiedCount = agents.Count(a => IsOccupied(a.Status));

            var countsByRank = agents
                .GroupBy(a => a.Rank)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var countsByStatus = agents
                .GroupBy(a => a.Status)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var payload = new
            {
                generated_at_utc = DateTime.UtcNow,
                total_agents = total,
                idle_agents = idleCount,
                occupied_agents = occupiedCount,
                counts_by_rank = countsByRank,
                counts_by_status = countsByStatus,
                agents = enriched
                    .OrderBy(a => a.rank)
                    .ThenBy(a => a.name)
                    .ToList()
            };

            var json = JsonSerializer.Serialize(payload, JsonDefaults.WebIndented);

            return new ToolResult
            {
                Success = true,
                Output = json,
                Metadata = new Dictionary<string, object>
                {
                    ["total_agents"] = total,
                    ["occupied_agents"] = occupiedCount,
                    ["idle_agents"] = idleCount
                }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get agent status");
            return new ToolResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message
            };
        }
    }

    private sealed record TaskSummary(string id, string status, string description, DateTime created_at_utc);

    private sealed record AgentStatusRow(
        string id,
        string name,
        string rank,
        string status,
        string occupancy,
        bool occupied,
        TaskSummary? working_on,
        TaskSummary? last_task);

    private async Task<AgentStatusRow> EnrichAsync(IAgent agent, CancellationToken ct)
    {
        TaskEntry? currentTask = null;
        TaskEntry? lastTask = null;

        try
        {
            var tasks = (await _sharedMemory.GetTasksByAgentAsync(agent.Id, ct).ConfigureAwait(false)).ToList();
            lastTask = tasks.FirstOrDefault();
            currentTask = tasks.FirstOrDefault(t => t.Status == CoreTaskStatus.InProgress)
                ?? tasks.FirstOrDefault(t => t.Status == CoreTaskStatus.Pending);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort enrichment only.
            _logger.LogDebug(ex, "Failed to enrich agent status with memory tasks for {AgentId}", agent.Id);
        }

        var occupied = IsOccupied(agent.Status);

        TaskSummary? workingOnSummary = currentTask != null
            ? new TaskSummary(
                currentTask.Id,
                currentTask.Status.ToString(),
                Truncate(currentTask.Description, 240),
                currentTask.CreatedAt)
            : null;

        TaskSummary? lastTaskSummary = lastTask != null
            ? new TaskSummary(
                lastTask.Id,
                lastTask.Status.ToString(),
                Truncate(lastTask.Description, 240),
                lastTask.CreatedAt)
            : null;

        return new AgentStatusRow(
            id: agent.Id,
            name: agent.Name,
            rank: agent.Rank.ToString(),
            status: agent.Status.ToString(),
            occupancy: occupied ? "Occupied" : "Idle",
            occupied: occupied,
            working_on: workingOnSummary,
            last_task: lastTaskSummary);
    }

    private static bool IsOccupied(AgentStatus status)
        => status is AgentStatus.Thinking or AgentStatus.ActingWithTool or AgentStatus.Waiting;

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLen)
        {
            return value;
        }

        return value[..maxLen] + "...";
    }
}
