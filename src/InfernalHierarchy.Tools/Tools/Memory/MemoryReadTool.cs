using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Tools.Tools.Memory;

/// <summary>
/// Tool for reading from shared memory
/// </summary>
public class MemoryReadTool : ITool
{
    private readonly ISharedMemory _memory;
    private readonly ILogger<MemoryReadTool> _logger;

    public string Name => "read_memory";
    public string Description => "Read from shared memory. Types: decisions, facts, tasks. Optional search query parameter.";

    public MemoryReadTool(ISharedMemory memory, ILogger<MemoryReadTool> logger)
    {
        _memory = memory;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("type", out var typeObj) || typeObj is not string type)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: type (decisions/facts/tasks)"
            };
        }

        // Extract agent context for visibility filtering
        parameters.TryGetValue("agent_id", out var agentIdObj);
        var agentId = agentIdObj as string ?? "unknown";
        
        parameters.TryGetValue("agent_rank", out var rankObj);
        var agentRank = ParseAgentRank(rankObj as string);

        parameters.TryGetValue("query", out var queryObj);
        var query = queryObj as string;

        parameters.TryGetValue("count", out var countObj);
        var count = countObj is int c ? c : 10;

        try
        {
            string output = type switch
            {
                var t when t.Equals("decisions", StringComparison.OrdinalIgnoreCase) => await GetDecisionsAsync(query, count, ct),
                var t when t.Equals("facts", StringComparison.OrdinalIgnoreCase) => await GetFactsAsync(query, agentId, agentRank, ct),
                var t when t.Equals("tasks", StringComparison.OrdinalIgnoreCase) => await GetTasksAsync(query, ct),
                _ => throw new ArgumentException($"Invalid type: {type}")
            };

            return new ToolResult
            {
                Success = true,
                Output = output,
                Metadata = new Dictionary<string, object> { ["type"] = type }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read memory: {Type}", type);
            return new ToolResult
            {
                Success = false,
                Error = $"Memory read failed: {ex.Message}"
            };
        }
    }

    private async Task<string> GetDecisionsAsync(string? query, int count, CancellationToken ct)
    {
        var decisions = string.IsNullOrEmpty(query)
            ? await _memory.GetRecentDecisionsAsync(count, ct)
            : await _memory.SearchDecisionsAsync(query, ct);

        if (!decisions.Any())
            return "No decisions found.";

        return string.Join("\n\n", decisions.Select(d =>
            $"[{d.CreatedAt:yyyy-MM-dd HH:mm}] {d.CreatedBy}: {d.Action}\nReasoning: {d.Reasoning}"));
    }

    private async Task<string> GetFactsAsync(string? query, string agentId, Core.Entities.AgentRank agentRank, CancellationToken ct)
    {
        var facts = string.IsNullOrEmpty(query)
            ? await _memory.GetVisibleFactsAsync(agentId, agentRank, ct)
            : await _memory.SearchVisibleFactsAsync(query, agentId, agentRank, ct);

        if (!facts.Any())
            return "No facts found.";

        return string.Join("\n\n", facts.Select(f =>
            $"[{f.Category}] {f.Content}\nSource: {f.Source} (Confidence: {f.Confidence:P0})"));
    }

    private async Task<string> GetTasksAsync(string? query, CancellationToken ct)
    {
        var tasks = string.IsNullOrEmpty(query)
            ? await _memory.GetTasksByStatusAsync(Core.Entities.TaskStatus.Pending, ct)
            : await _memory.GetTasksByAgentAsync(query, ct);

        if (!tasks.Any())
            return "No tasks found.";

        return string.Join("\n\n", tasks.Select(t =>
            $"[{t.Status}] {t.Description}\nAssigned to: {t.AssignedTo}"));
    }

    private Core.Entities.AgentRank ParseAgentRank(string? rankString)
    {
        if (string.IsNullOrEmpty(rankString))
            return Core.Entities.AgentRank.Worker; // Default to least privileged

        return Enum.TryParse<Core.Entities.AgentRank>(rankString, true, out var rank)
            ? rank
            : Core.Entities.AgentRank.Worker;
    }
}
