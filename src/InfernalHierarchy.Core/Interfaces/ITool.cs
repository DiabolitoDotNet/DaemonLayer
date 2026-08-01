namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Base interface for all executable tools.
/// Tools are the system's controlled capability surface: agents request tool execution,
/// and the host/pipeline enforces authorization, throttling, and observability.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Stable tool identifier used for authorization, routing, and telemetry.
    /// This should remain constant over time.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable explanation of what the tool does and when to use it.
    /// Intended for agents and operator-facing documentation.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the tool.
    /// Implementations should be resilient and return a successful <see cref="ToolResult"/>
    /// whenever they can produce a meaningful output.
    /// </summary>
    /// <param name="parameters">Structured tool parameters (validated by the pipeline and/or the tool).</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A <see cref="ToolResult"/> containing output, error (if any), and metadata.</returns>
    Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default);
}

/// <summary>
/// Standard result object returned by <see cref="ITool"/> execution.
/// </summary>
public class ToolResult
{
    /// <summary>
    /// Indicates whether the tool execution succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Tool output intended to be consumable by an agent and/or operator.
    /// For structured outputs, encode as JSON (and include a content-type hint in <see cref="Metadata"/> when relevant).
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// Optional error message when <see cref="Success"/> is false.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Optional structured metadata (durations, ids, counters, content-type hints, etc.).
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Registry for managing available tools.
/// Acts as the discovery mechanism for tool lookup and execution.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Registers a tool instance.
    /// </summary>
    void RegisterTool(ITool tool);

    /// <summary>
    /// Gets a tool by its <see cref="ITool.Name"/>.
    /// </summary>
    ITool? GetTool(string name);

    /// <summary>
    /// Lists all registered tools.
    /// </summary>
    IEnumerable<ITool> GetAllTools();

    /// <summary>
    /// Returns tool instances corresponding to the provided tool names.
    /// Typically used when a persona restricts the available tool set.
    /// </summary>
    IEnumerable<ITool> GetToolsForAgent(string[] toolNames);

    /// <summary>
    /// Executes a tool by name while capturing execution telemetry/metadata.
    /// Implementations may apply authorization, rate limiting, retries, and auditing.
    /// </summary>
    Task<ToolResult> ExecuteToolWithTrackingAsync(
        string toolName,
        Dictionary<string, object> parameters,
        string? agentId = null,
        string? agentRank = null,
        string? agentName = null,
        CancellationToken ct = default);
}
