
namespace InfernalHierarchy.Tools.Tools.Memory;

/// <summary>
/// Tool for writing to shared memory
/// </summary>
public class MemoryWriteTool : ITool
{
    private readonly ISharedMemory _memory;
    private readonly IVectorMemory? _vectorMemory;
    private readonly ILogger<MemoryWriteTool> _logger;

    public string Name => "write_memory";
    public string Description => "Write to shared memory. Types: decision, fact, task. Requires type-specific parameters. For facts: optional visibility (Private/RankBased/Shared/Public), shared_with (comma-separated agent IDs), min_rank (Supreme/Prince/Duke/Worker).";

    public MemoryWriteTool(ISharedMemory memory, ILogger<MemoryWriteTool> logger)
        : this(memory, vectorMemory: null, logger)
    {
    }

    public MemoryWriteTool(ISharedMemory memory, IVectorMemory? vectorMemory, ILogger<MemoryWriteTool> logger)
    {
        _memory = memory;
        _vectorMemory = vectorMemory;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("type", out var typeObj) || typeObj is not string type)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: type (decision/fact/task)"
            };
        }

        if (!parameters.TryGetValue("agent_id", out var agentIdObj) || agentIdObj is not string agentId)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: agent_id"
            };
        }

        if (!type.Equals("decision", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("fact", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("task", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Invalid type: {type}. Expected: decision/fact/task"
            };
        }

        try
        {
            var output = type switch
            {
                var t when t.Equals("decision", StringComparison.OrdinalIgnoreCase) => await WriteDecisionAsync(parameters, agentId, ct),
                var t when t.Equals("fact", StringComparison.OrdinalIgnoreCase) => await WriteFactAsync(parameters, agentId, ct),
                var t when t.Equals("task", StringComparison.OrdinalIgnoreCase) => await WriteTaskAsync(parameters, agentId, ct),
                _ => ""
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
            _logger.LogError(ex, "Failed to write memory: {Type}", type);
            return new ToolResult
            {
                Success = false,
                Error = $"Memory write failed: {ex.Message}"
            };
        }
    }

    private async Task<string> WriteDecisionAsync(Dictionary<string, object> parameters, string agentId, CancellationToken ct)
    {
        var context = parameters.GetValueOrDefault("context") as string ?? "";
        var action = parameters.GetValueOrDefault("action") as string ?? "";
        var reasoning = parameters.GetValueOrDefault("reasoning") as string ?? "";

        var decision = new Decision
        {
            CreatedBy = agentId,
            Context = context,
            Action = action,
            Reasoning = reasoning
        };

        await _memory.AddDecisionAsync(decision, ct);
        return $"Decision recorded: {action}";
    }

    private async Task<string> WriteFactAsync(Dictionary<string, object> parameters, string agentId, CancellationToken ct)
    {
        var category = parameters.GetValueOrDefault("category") as string ?? "general";
        var content = parameters.GetValueOrDefault("content") as string ?? "";
        var source = parameters.GetValueOrDefault("source") as string ?? "agent";

        var confidenceObj = parameters.GetValueOrDefault("confidence");
        var confidence = confidenceObj is double d ? d : 1.0;

        // Support visibility settings (default: Private)
        var visibilityStr = parameters.GetValueOrDefault("visibility") as string;
        var visibility = ParseVisibility(visibilityStr);

        var sharedWithStr = parameters.GetValueOrDefault("shared_with") as string;
        var sharedWith = string.IsNullOrEmpty(sharedWithStr)
            ? new List<string>()
            : sharedWithStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var minRankStr = parameters.GetValueOrDefault("min_rank") as string;
        AgentRank? minRank = null;
        if (!string.IsNullOrEmpty(minRankStr) && Enum.TryParse<AgentRank>(minRankStr, true, out var parsedRank))
        {
            minRank = parsedRank;
        }

        var fact = new Fact
        {
            CreatedBy = agentId,
            Category = category,
            Content = content,
            Source = source,
            Confidence = confidence,
            Visibility = visibility,
            SharedWithAgents = sharedWith,
            MinimumRankToView = minRank
        };

        if (_vectorMemory != null)
        {
            await _vectorMemory.IndexFactAsync(fact, ct);
        }
        else
        {
            await _memory.AddFactAsync(fact, ct);
        }
        
        var visibilityInfo = visibility switch
        {
            MemoryVisibility.Public => " (public - visible to all)",
            MemoryVisibility.RankBased => $" (rank-based - {minRank}+)",
            MemoryVisibility.Shared => $" (shared with: {string.Join(", ", sharedWith)})",
            _ => " (private)"
        };
        
        return $"Fact recorded in category: {category}{visibilityInfo}";
    }

    private MemoryVisibility ParseVisibility(string? visibilityStr)
    {
        if (string.IsNullOrEmpty(visibilityStr))
            return MemoryVisibility.Private; // Default: private

        return Enum.TryParse<MemoryVisibility>(visibilityStr, true, out var visibility)
            ? visibility
            : MemoryVisibility.Private;
    }

    private async Task<string> WriteTaskAsync(Dictionary<string, object> parameters, string agentId, CancellationToken ct)
    {
        var description = parameters.GetValueOrDefault("description") as string ?? "";
        var assignedTo = parameters.GetValueOrDefault("assigned_to") as string ?? agentId;

        var task = new TaskEntry
        {
            CreatedBy = agentId,
            Description = description,
            AssignedTo = assignedTo,
            Status = Core.Entities.TaskStatus.Pending
        };

        await _memory.AddTaskAsync(task, ct);
        return $"Task created and assigned to: {assignedTo}";
    }
}
