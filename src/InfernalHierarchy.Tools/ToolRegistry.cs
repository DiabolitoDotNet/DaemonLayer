using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace InfernalHierarchy.Tools;

/// <summary>
/// Registry for managing and accessing agent tools
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterTool(ITool tool)
    {
        if (_tools.TryAdd(tool.Name.ToLower(), tool))
        {
            _logger.LogInformation("🔧 Registered tool: {ToolName}", tool.Name);
        }
        else
        {
            _logger.LogWarning("⚠️ Tool {ToolName} already registered", tool.Name);
        }
    }

    public ITool? GetTool(string name)
    {
        _tools.TryGetValue(name.ToLower(), out var tool);
        return tool;
    }

    public IEnumerable<ITool> GetAllTools() => _tools.Values;

    public IEnumerable<ITool> GetToolsForAgent(string[] toolNames)
    {
        var tools = new List<ITool>();

        foreach (var name in toolNames)
        {
            var tool = GetTool(name);
            if (tool != null)
            {
                tools.Add(tool);
            }
            else
            {
                _logger.LogWarning("Tool {ToolName} not found in registry", name);
            }
        }

        return tools;
    }
}
