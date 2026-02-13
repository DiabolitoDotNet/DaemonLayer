using System.Text;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.Tools.Tools.Meta;

public sealed class GetCustomToolSourceTool : ITool
{
    private readonly ICustomToolStore _store;

    public string Name => "custom_tool_get_source";
    public string Description => "Get persisted custom tool source code from LiteDB. Params: tool_name.";

    public GetCustomToolSourceTool(ICustomToolStore store)
    {
        _store = store;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (parameters is null)
        {
            return new ToolResult { Success = false, Error = "Missing parameters" };
        }

        var toolName = parameters.GetValueOrDefault("tool_name")?.ToString()
            ?? parameters.GetValueOrDefault("tool")?.ToString()
            ?? parameters.GetValueOrDefault("name")?.ToString();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return new ToolResult { Success = false, Error = "Missing required parameter: tool_name" };
        }

        CustomToolDefinition? def;
        try
        {
            def = await _store.GetByNameAsync(toolName.Trim(), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = "Failed to read custom tool definition", Output = ex.Message };
        }

        if (def is null)
        {
            return new ToolResult { Success = false, Error = $"Custom tool '{toolName}' not found in store" };
        }

        var sb = new StringBuilder();
        sb.AppendLine($"tool_name: {def.ToolName}");
        sb.AppendLine($"tool_id: {def.Id}");
        sb.AppendLine($"source_hash: {def.SourceHash}");
        sb.AppendLine($"requires_manual_approval: {def.RequiresManualApproval}");
        sb.AppendLine($"created_at_utc: {def.CreatedAt:O}");
        sb.AppendLine($"created_by_agent: {def.CreatedByAgentName} ({def.CreatedByAgentId})");

        if (def.LastCompiledAt is not null)
        {
            sb.AppendLine($"last_compiled_at_utc: {def.LastCompiledAt:O}");
        }

        if (!string.IsNullOrWhiteSpace(def.LastCompileError))
        {
            sb.AppendLine("last_compile_error:");
            sb.AppendLine(def.LastCompileError);
        }

        sb.AppendLine("---SOURCE---");
        sb.AppendLine(def.SourceCode ?? string.Empty);

        return new ToolResult
        {
            Success = true,
            Output = sb.ToString(),
            Metadata = new Dictionary<string, object>
            {
                ["tool_name"] = def.ToolName,
                ["tool_id"] = def.Id,
                ["source_hash"] = def.SourceHash,
                ["requires_manual_approval"] = def.RequiresManualApproval
            }
        };
    }
}
