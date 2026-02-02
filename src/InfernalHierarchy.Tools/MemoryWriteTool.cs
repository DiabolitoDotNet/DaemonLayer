using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Tools;

/// <summary>
/// Tool for writing to shared memory
/// </summary>
public class MemoryWriteTool : ITool
{
    private readonly ISharedMemory _memory;
    private readonly ILogger<MemoryWriteTool> _logger;

    public string Name => "write_memory";
    public string Description => "Write to shared memory. Types: decision, fact, task. Requires type-specific parameters.";

    public MemoryWriteTool(ISharedMemory memory, ILogger<MemoryWriteTool> logger)
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

        try
        {
            var output = type.ToLower() switch
            {
                "decision" => await WriteDecisionAsync(parameters, agentId, ct),
                "fact" => await WriteFactAsync(parameters, agentId, ct),
                "task" => await WriteTaskAsync(parameters, agentId, ct),
                _ => throw new ArgumentException($"Invalid type: {type}")
            };

            return new ToolResult
            {
                Success = true,
                Output = output,
                Metadata = new Dictionary<string, object> { ["type"] = type }
            };
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

        var fact = new Fact
        {
            CreatedBy = agentId,
            Category = category,
            Content = content,
            Source = source,
            Confidence = confidence
        };

        await _memory.AddFactAsync(fact, ct);
        return $"Fact recorded in category: {category}";
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
