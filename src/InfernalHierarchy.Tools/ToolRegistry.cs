using InfernalHierarchy.Core;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace InfernalHierarchy.Tools;

/// <summary>
/// Registry for managing and accessing agent tools
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;
    private readonly AgentLearningService? _learningService;
    private readonly IServiceProvider? _serviceProvider;

    public ToolRegistry(
        ILogger<ToolRegistry> logger,
        AgentLearningService? learningService = null,
        IServiceProvider? serviceProvider = null)
    {
        _logger = logger;
        _learningService = learningService;
        _serviceProvider = serviceProvider;
    }

    public void RegisterTool(ITool tool)
    {
        var normalizedName = tool.Name.ToLowerInvariant();
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
        _tools.TryGetValue(name.ToLowerInvariant(), out var tool);
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

    /// <summary>
    /// Get a service from the service provider (for command handlers)
    /// </summary>
    public T? GetService<T>() where T : class
    {
        return _serviceProvider?.GetService<T>();
    }

    /// <summary>
    /// Execute a tool with automatic performance tracking
    /// </summary>
    public async Task<ToolResult> ExecuteToolWithTrackingAsync(
        string toolName,
        Dictionary<string, object> parameters,
        string? agentId = null,
        string? agentRank = null,
        CancellationToken ct = default)
    {
        var tool = GetTool(toolName);
        if (tool == null)
        {
            return new ToolResult
            {
                Success = false,
                Output = $"Tool '{toolName}' not found",
                Error = "Tool not found in registry"
            };
        }

        var stopwatch = Stopwatch.StartNew();
        ToolResult result;

        try
        {
            // Get GlobalExceptionHandler if available for retry logic
            var exceptionHandler = _serviceProvider?.GetService<GlobalExceptionHandler>();
            
            if (exceptionHandler != null)
            {
                // Use centralized exception handling with automatic retry
                result = await exceptionHandler.ExecuteWithHandlingAsync(
                    async (cancellationToken) => await tool.ExecuteAsync(parameters, cancellationToken),
                    $"Tool_{toolName}_{agentId}",
                    ct: ct);
            }
            else
            {
                // Fallback to direct execution
                result = await tool.ExecuteAsync(parameters, ct);
            }
            
            stopwatch.Stop();

            // Record execution in learning service
            if (_learningService != null && agentId != null)
            {
                _learningService.RecordToolExecution(
                    agentId,
                    agentRank ?? "Worker",
                    toolName,
                    result.Success,
                    stopwatch.Elapsed);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Use centralized exception handling if available
            var exceptionHandler = _serviceProvider?.GetService<GlobalExceptionHandler>();
            if (exceptionHandler != null)
            {
                var handlingResult = await exceptionHandler.HandleExceptionAsync(
                    ex,
                    $"Tool_{toolName}_{agentId}");
                
                _logger.LogError(
                    ex,
                    "🔥 Tool {ToolName} failed | Category: {Category} | Retry: {ShouldRetry} | CorrelationId: {CorrelationId}",
                    toolName,
                    handlingResult.Category,
                    handlingResult.ShouldRetry,
                    handlingResult.CorrelationId);
            }
            else
            {
                _logger.LogError(ex, "Tool {ToolName} execution failed", toolName);
            }

            // Record failure
            if (_learningService != null && agentId != null)
            {
                _learningService.RecordToolExecution(
                    agentId,
                    agentRank ?? "Worker",
                    toolName,
                    false,
                    stopwatch.Elapsed);
            }

            return new ToolResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message
            };
        }
    }
}
