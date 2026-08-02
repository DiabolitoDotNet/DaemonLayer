using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfernalHierarchy.Tools.Execution;

/// <summary>
/// Registry for managing and accessing agent tools
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;
    private readonly IToolExecutionPipeline _pipeline;

    public ToolRegistry(
        ILogger<ToolRegistry> logger,
        AgentLearningService? learningService = null,
        IServiceProvider? serviceProvider = null,
        IAgentEventSink? eventSink = null,
        IToolExecutionPipeline? pipeline = null)
    {
        _logger = logger;
        _pipeline = pipeline ?? new DefaultToolExecutionPipeline(
            NullLogger<DefaultToolExecutionPipeline>.Instance,
            learningService,
            exceptionHandler: null,
            eventSink: eventSink);
    }

    public void RegisterTool(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var normalizedName = NormalizeToolName(tool.Name);
        if (normalizedName is null)
        {
            _logger.LogWarning("Ignoring registration for tool with empty name");
            return;
        }

        // Custom tools are dynamic and may be regenerated/overwritten at runtime.
        // For these, we must allow replacing the existing implementation.
        if (normalizedName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _tools.ContainsKey(normalizedName);
            _tools[normalizedName] = tool;

            if (existed)
            {
                _logger.LogInformation("🔁 Updated tool: {ToolName}", tool.Name);
            }
            else
            {
                _logger.LogInformation("🔧 Registered tool: {ToolName}", tool.Name);
            }

            return;
        }

        if (_tools.TryAdd(normalizedName, tool))
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
        var normalizedName = NormalizeToolName(name);
        if (normalizedName is null)
        {
            return null;
        }

        _tools.TryGetValue(normalizedName, out var tool);
        return tool;
    }

    public bool UnregisterTool(string name)
    {
        var normalizedName = NormalizeToolName(name);
        if (normalizedName is null)
        {
            return false;
        }

        if (_tools.TryRemove(normalizedName, out _))
        {
            _logger.LogInformation("🧹 Unregistered tool: {ToolName}", normalizedName);
            return true;
        }

        return false;
    }

    public IEnumerable<ITool> GetAllTools() => _tools.Values;

    public IEnumerable<ITool> GetToolsForAgent(string[] toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        var tools = new List<ITool>();

        foreach (var name in toolNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

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

    /// <summary>
    /// Execute a tool with automatic performance tracking
    /// </summary>
    public async Task<ToolResult> ExecuteToolWithTrackingAsync(
        string toolName,
        Dictionary<string, object> parameters,
        string? agentId = null,
        string? agentRank = null,
        string? agentName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var normalizedToolName = NormalizeToolName(toolName);
        if (normalizedToolName is null)
        {
            return new ToolResult
            {
                Success = false,
                Output = string.Empty,
                Error = "Tool name is required"
            };
        }

        var tool = GetTool(normalizedToolName);
        if (tool == null)
        {
            return new ToolResult
            {
                Success = false,
                Output = $"Tool '{normalizedToolName}' not found",
                Error = "Tool not found in registry"
            };
        }

        return await _pipeline.ExecuteAsync(new ToolExecutionContext(
            ToolName: normalizedToolName,
            Tool: tool,
            Parameters: parameters,
            AgentId: agentId,
            AgentRank: agentRank,
            CancellationToken: ct,
            AgentName: agentName)).ConfigureAwait(false);
    }

    private static string? NormalizeToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        return toolName.Trim().ToLowerInvariant();
    }
}
