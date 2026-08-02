using System.Text.Json;

namespace InfernalHierarchy.Tools.Tools.Meta;

public sealed class ListCustomToolsTool : ITool
{
    private readonly ICustomToolStore _store;
    private readonly IToolRegistry _registry;

    public ListCustomToolsTool(ICustomToolStore store, IToolRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public string Name => "custom_tool_list";

    public string Description => "List persisted custom tools with approval and runtime registration status.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var tools = await _store.GetAllAsync(ct).ConfigureAwait(false);
        var loaded = _registry.GetAllTools().Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var payload = tools.Select(t => new
        {
            id = t.Id,
            tool_name = t.ToolName,
            description = t.Description,
            created_at_utc = t.CreatedAt,
            created_by = t.CreatedByAgentName,
            requires_manual_approval = t.RequiresManualApproval,
            last_compiled_at_utc = t.LastCompiledAt,
            last_compile_error = t.LastCompileError,
            is_loaded = loaded.Contains(t.ToolName)
        });

        return new ToolResult
        {
            Success = true,
            Output = JsonSerializer.Serialize(payload),
            Metadata = new Dictionary<string, object>
            {
                ["count"] = tools.Count
            }
        };
    }
}