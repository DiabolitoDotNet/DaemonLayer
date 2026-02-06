using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Serialization;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using InfernalHierarchy.Tools.Learning;

namespace InfernalHierarchy.Tools.Execution;

public sealed class DefaultToolExecutionPipeline : IToolExecutionPipeline
{
    private readonly ILogger<DefaultToolExecutionPipeline> _logger;
    private readonly AgentLearningService? _learningService;
    private readonly GlobalExceptionHandler? _exceptionHandler;
    private readonly IAgentEventSink? _eventSink;
    private readonly IToolRateLimiter? _rateLimiter;
    private readonly IToolAuthorizationService? _authorizationService;

    public DefaultToolExecutionPipeline(
        ILogger<DefaultToolExecutionPipeline> logger,
        AgentLearningService? learningService = null,
        GlobalExceptionHandler? exceptionHandler = null,
        IAgentEventSink? eventSink = null,
        IToolRateLimiter? rateLimiter = null,
        IToolAuthorizationService? authorizationService = null)
    {
        _logger = logger;
        _learningService = learningService;
        _exceptionHandler = exceptionHandler;
        _eventSink = eventSink;
        _rateLimiter = rateLimiter;
        _authorizationService = authorizationService;
    }

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context)
    {
        if (_authorizationService != null && !string.IsNullOrWhiteSpace(context.AgentId))
        {
            var rank = AgentRank.Worker;
            if (!string.IsNullOrWhiteSpace(context.AgentRank) &&
                Enum.TryParse(context.AgentRank, ignoreCase: true, out AgentRank parsedRank))
            {
                rank = parsedRank;
            }

            var agentName = !string.IsNullOrWhiteSpace(context.AgentName)
                ? context.AgentName
                : context.AgentId;

            var decision = _authorizationService.IsAuthorized(
                context.AgentId,
                agentName,
                rank,
                context.ToolName);

            if (!decision.IsAuthorized)
            {
                var denied = new ToolResult
                {
                    Success = false,
                    Output = string.Empty,
                    Error = string.IsNullOrWhiteSpace(decision.Reason)
                        ? "Access denied"
                        : $"Access denied: {decision.Reason}",
                    Metadata = new Dictionary<string, object>
                    {
                        ["authorization_denied"] = true,
                        ["tool"] = context.ToolName,
                        ["agent_rank"] = context.AgentRank ?? rank.ToString()
                    }
                };

                TryAppendToolEvent(
                    context,
                    success: false,
                    duration: TimeSpan.Zero,
                    errorMessage: denied.Error);

                return denied;
            }
        }

        if (_rateLimiter != null)
        {
            var decision = _rateLimiter.Check(context);
            if (!decision.Allowed)
            {
                var result = new ToolResult
                {
                    Success = false,
                    Output = string.Empty,
                    Error = string.IsNullOrWhiteSpace(decision.Reason)
                        ? $"Rate limit exceeded. Retry after {(int)Math.Ceiling(decision.RetryAfter.TotalSeconds)}s."
                        : $"{decision.Reason}. Retry after {(int)Math.Ceiling(decision.RetryAfter.TotalSeconds)}s.",
                    Metadata = new Dictionary<string, object>
                    {
                        ["rate_limited"] = true,
                        ["retry_after_ms"] = (long)decision.RetryAfter.TotalMilliseconds
                    }
                };

                TryAppendToolEvent(
                    context,
                    success: false,
                    duration: TimeSpan.Zero,
                    errorMessage: result.Error);

                return result;
            }
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            ToolResult result;

            if (_exceptionHandler != null)
            {
                result = await _exceptionHandler.ExecuteWithHandlingAsync(
                    async cancellationToken => await context.Tool.ExecuteAsync(context.Parameters, cancellationToken).ConfigureAwait(false),
                    $"Tool_{context.ToolName}_{context.AgentId}",
                    ct: context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await context.Tool.ExecuteAsync(context.Parameters, context.CancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();

            if (_learningService != null && context.AgentId != null)
            {
                _learningService.RecordToolExecution(
                    context.AgentId,
                    context.AgentRank ?? "Worker",
                    context.ToolName,
                    result.Success,
                    stopwatch.Elapsed);
            }

            TryAppendToolEvent(
                context,
                success: result.Success,
                duration: stopwatch.Elapsed,
                errorMessage: result.Success ? null : (result.Error ?? "Tool returned failure"));

            return result;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            if (_exceptionHandler != null)
            {
                var handlingResult = await _exceptionHandler.HandleExceptionAsync(
                    ex,
                    $"Tool_{context.ToolName}_{context.AgentId}").ConfigureAwait(false);

                _logger.LogError(
                    ex,
                    "🔥 Tool {ToolName} failed | Category: {Category} | Retry: {ShouldRetry} | CorrelationId: {CorrelationId}",
                    context.ToolName,
                    handlingResult.Category,
                    handlingResult.ShouldRetry,
                    handlingResult.CorrelationId);
            }
            else
            {
                _logger.LogError(ex, "Tool {ToolName} execution failed", context.ToolName);
            }

            if (_learningService != null && context.AgentId != null)
            {
                _learningService.RecordToolExecution(
                    context.AgentId,
                    context.AgentRank ?? "Worker",
                    context.ToolName,
                    success: false,
                    stopwatch.Elapsed);
            }

            TryAppendToolEvent(
                context,
                success: false,
                duration: stopwatch.Elapsed,
                errorMessage: ex.Message);

            return new ToolResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message
            };
        }
    }

    private void TryAppendToolEvent(
        ToolExecutionContext context,
        bool success,
        TimeSpan duration,
        string? errorMessage)
    {
        if (_eventSink == null || string.IsNullOrWhiteSpace(context.AgentId))
        {
            return;
        }

        var safeParametersJson = SafeSerialize(context.Parameters);

        var evt = new AgentEvent
        {
            AgentId = context.AgentId,
            Type = success ? EventType.ToolExecuted : EventType.ErrorOccurred,
            Description = success
                ? $"Tool executed: {context.ToolName}"
                : $"Tool failed: {context.ToolName}",
            Metadata = new Dictionary<string, object>
            {
                ["tool"] = context.ToolName,
                ["success"] = success,
                ["duration_ms"] = (long)duration.TotalMilliseconds,
                ["agent_rank"] = context.AgentRank ?? "Worker",
                ["parameters_json"] = safeParametersJson
            }
        };

        if (!success && !string.IsNullOrWhiteSpace(errorMessage))
        {
            evt.Metadata["error"] = errorMessage;
        }

        try
        {
            _eventSink.AppendEvent(evt);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to append tool event");
        }
    }

    private static string SafeSerialize(Dictionary<string, object> parameters)
    {
        try
        {
            return JsonSerializer.Serialize(parameters, JsonDefaults.Web);
        }
        catch
        {
            return string.Join(", ",
                parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
    }
}
