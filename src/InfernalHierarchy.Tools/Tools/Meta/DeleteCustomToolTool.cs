namespace InfernalHierarchy.Tools.Tools.Meta;

public sealed class DeleteCustomToolTool : ITool
{
    private readonly ICustomToolStore _store;
    private readonly IToolRegistry _registry;

    public DeleteCustomToolTool(ICustomToolStore store, IToolRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public string Name => "custom_tool_delete";

    public string Description => "Delete a persisted custom tool and optionally remove it from the runtime registry. Params: tool_name or tool_id, unregister (optional, default true).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var toolName = GetString(parameters, "tool_name") ?? GetString(parameters, "name");
        var toolId = GetString(parameters, "tool_id") ?? GetString(parameters, "id");
        var unregister = GetBool(parameters, "unregister", defaultValue: true);

        if (string.IsNullOrWhiteSpace(toolName) && string.IsNullOrWhiteSpace(toolId))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: tool_name or tool_id",
                Output = string.Empty
            };
        }

        var definition = !string.IsNullOrWhiteSpace(toolId)
            ? await _store.GetByIdAsync(toolId.Trim(), ct).ConfigureAwait(false)
            : await _store.GetByNameAsync(toolName!.Trim(), ct).ConfigureAwait(false);

        if (definition is null)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Custom tool not found",
                Output = string.Empty
            };
        }

        var removed = await _store.DeleteByIdAsync(definition.Id, ct).ConfigureAwait(false);
        var unregistered = unregister && _registry.UnregisterTool(definition.ToolName);

        return new ToolResult
        {
            Success = removed,
            Output = removed
                ? $"Deleted custom tool '{definition.ToolName}'"
                : $"Failed to delete custom tool '{definition.ToolName}'",
            Error = removed ? null : "Delete failed",
            Metadata = new Dictionary<string, object>
            {
                ["tool_id"] = definition.Id,
                ["tool_name"] = definition.ToolName,
                ["deleted"] = removed,
                ["unregistered"] = unregistered
            }
        };
    }

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }

    private static bool GetBool(Dictionary<string, object> parameters, string key, bool defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
    }
}