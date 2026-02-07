namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Cross-cutting execution pipeline for tools (authorization, validation, retry, metrics, auditing, learning, etc.).
/// </summary>
public interface IToolExecutionPipeline
{
    /// <summary>
    /// Executes a tool call with cross-cutting concerns applied.
    /// </summary>
    /// <param name="context">Execution context (tool identity, agent metadata, parameters, cancellation).</param>
    Task<ToolResult> ExecuteAsync(ToolExecutionContext context);
}

/// <summary>
/// Immutable context passed to <see cref="IToolExecutionPipeline"/>.
/// </summary>
/// <param name="ToolName">Tool name for routing/telemetry (usually <see cref="ITool.Name"/>).</param>
/// <param name="Tool">Tool instance.</param>
/// <param name="Parameters">Tool parameters.</param>
/// <param name="AgentId">Optional agent id executing the tool.</param>
/// <param name="AgentRank">Optional agent rank (stringified).</param>
/// <param name="CancellationToken">Cancellation token for cooperative cancellation.</param>
/// <param name="AgentName">Optional agent display name.</param>
public sealed record ToolExecutionContext(
    string ToolName,
    ITool Tool,
    Dictionary<string, object> Parameters,
    string? AgentId,
    string? AgentRank,
    CancellationToken CancellationToken,
    string? AgentName = null);
