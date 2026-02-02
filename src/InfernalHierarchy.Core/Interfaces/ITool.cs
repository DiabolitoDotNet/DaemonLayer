namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Base interface for agent tools
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }

    Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default);
}

public class ToolResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Registry to manage available tools
/// </summary>
public interface IToolRegistry
{
    void RegisterTool(ITool tool);
    ITool? GetTool(string name);
    IEnumerable<ITool> GetAllTools();
    IEnumerable<ITool> GetToolsForAgent(string[] toolNames);
}
