using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Serialization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Tools.Execution;

public sealed class DefaultToolExecutionPipeline : IToolExecutionPipeline
{
    private readonly ILogger<DefaultToolExecutionPipeline> _logger;
    private readonly AgentLearningService? _learningService;
    private readonly GlobalExceptionHandler? _exceptionHandler;
    private readonly IAgentEventSink? _eventSink;
    private readonly IToolRateLimiter? _rateLimiter;
    private readonly IToolAuthorizationService? _authorizationService;
    private readonly IToolResultCacheStore? _cacheStore;
    private readonly IOptions<ToolResultCacheOptions>? _cacheOptions;
    private readonly IToolExecutionLimiter? _executionLimiter;
    private readonly IFailedOperationStore? _failedOperationStore;
    private readonly ICapabilityOutcomePublisher? _outcomePublisher;

    public DefaultToolExecutionPipeline(
        ILogger<DefaultToolExecutionPipeline> logger,
        AgentLearningService? learningService = null,
        GlobalExceptionHandler? exceptionHandler = null,
        IAgentEventSink? eventSink = null,
        IToolRateLimiter? rateLimiter = null,
        IToolAuthorizationService? authorizationService = null,
        IToolResultCacheStore? cacheStore = null,
        IOptions<ToolResultCacheOptions>? cacheOptions = null,
        IToolExecutionLimiter? executionLimiter = null,
        IFailedOperationStore? failedOperationStore = null,
        ICapabilityOutcomePublisher? outcomePublisher = null)
    {
        _logger = logger;
        _learningService = learningService;
        _exceptionHandler = exceptionHandler;
        _eventSink = eventSink;
        _rateLimiter = rateLimiter;
        _authorizationService = authorizationService;
        _cacheStore = cacheStore;
        _cacheOptions = cacheOptions;
        _executionLimiter = executionLimiter;
        _failedOperationStore = failedOperationStore;
        _outcomePublisher = outcomePublisher;
    }

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Tool);
        ArgumentNullException.ThrowIfNull(context.Parameters);

        var canonicalToolName = ResolveCanonicalToolName(context);
        if (string.IsNullOrWhiteSpace(canonicalToolName))
        {
            throw new ArgumentException("Tool name is required", nameof(context));
        }

        var immutableParameters = CloneParameters(context.Parameters);
        var executionParameters = CloneParameters(immutableParameters);
        var cacheBypassRequested = IsCacheBypassRequested(immutableParameters);
        string? cacheKey = null;

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
                canonicalToolName);

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
                        ["tool"] = canonicalToolName,
                        ["agent_rank"] = context.AgentRank ?? rank.ToString()
                    }
                };

                TryAppendToolEvent(
                    context,
                    canonicalToolName,
                    immutableParameters,
                    success: false,
                    duration: TimeSpan.Zero,
                    errorMessage: denied.Error);

                return denied;
            }
        }

        // Cache lookup happens after authorization but before rate limiting.
        // This prevents expensive tools from being denied when a valid cached result exists.
        // If a tool is not cache-eligible, this is a no-op.
        if (_cacheStore != null && _cacheOptions?.Value.Enabled == true)
        {
            try
            {
                var cacheSettings = _cacheOptions.Value;
                if (TryResolveCachePolicy(canonicalToolName, cacheSettings, out var ttl) && !cacheBypassRequested)
                {
                    cacheKey = ComputeStableCacheKey(canonicalToolName, immutableParameters);
                    var cached = await _cacheStore.GetAsync(cacheKey, context.CancellationToken).ConfigureAwait(false);
                    if (cached != null)
                    {
                        var cachedResult = TryDeserializeToolResult(cached.ResultJson);
                        if (cachedResult != null)
                        {
                            cachedResult.Metadata["cache_hit"] = true;
                            cachedResult.Metadata["cache_key"] = cacheKey;
                            cachedResult.Metadata["cache_expires_at_utc"] = cached.ExpiresAt.ToString("O");
                            cachedResult.Metadata["cache_ttl_seconds"] = (long)ttl.TotalSeconds;
                            cachedResult.Metadata["tool"] = canonicalToolName;

                            TryAppendToolEvent(
                                context,
                                canonicalToolName,
                                immutableParameters,
                                success: cachedResult.Success,
                                duration: TimeSpan.Zero,
                                errorMessage: cachedResult.Success ? null : (cachedResult.Error ?? "Cached tool failure"));

                            return cachedResult;
                        }

                        // If we cannot deserialize, treat it as a miss and remove the bad entry.
                        await _cacheStore.RemoveAsync(cacheKey, context.CancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Tool cache lookup failed for {ToolName}", canonicalToolName);
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
                    canonicalToolName,
                    immutableParameters,
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
                    async cancellationToken => await ExecuteToolCoreAsync(context.Tool, executionParameters, cancellationToken).ConfigureAwait(false),
                    $"Tool_{canonicalToolName}_{context.AgentId}",
                    ct: context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await ExecuteToolCoreAsync(context.Tool, executionParameters, context.CancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();

            if (_learningService != null && context.AgentId != null)
            {
                _learningService.RecordToolExecution(
                    context.AgentId,
                    context.AgentRank ?? "Worker",
                    canonicalToolName,
                    result.Success,
                    stopwatch.Elapsed);
            }

            TryAppendToolEvent(
                context,
                canonicalToolName,
                immutableParameters,
                success: result.Success,
                duration: stopwatch.Elapsed,
                errorMessage: result.Success ? null : (result.Error ?? "Tool returned failure"));

            if (!result.Success)
            {
                await RecordToolFailureDeadLetterAsync(
                    context,
                    canonicalToolName,
                    immutableParameters,
                    "tool_result_failed",
                    result.Error ?? "Tool returned failure",
                    context.CancellationToken).ConfigureAwait(false);
            }
            else if (_outcomePublisher is not null && canonicalToolName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
            {
                await _outcomePublisher.RecordOutcomeAsync(new CapabilityOutcome
                {
                    Kind = CapabilityOutcomeKind.CustomToolExecutionSucceeded,
                    CapabilityId = canonicalToolName,
                    CapabilityType = "custom_tool",
                    SourceTask = string.Empty,
                    RiskLevel = "Medium",
                    AgentId = context.AgentId ?? string.Empty,
                    OccurredAtUtc = DateTime.UtcNow
                }, context.CancellationToken).ConfigureAwait(false);
            }

            // Cache store happens after real execution.
            if (_cacheStore != null && _cacheOptions?.Value.Enabled == true)
            {
                try
                {
                    var cacheSettings = _cacheOptions.Value;
                    if (TryResolveCachePolicy(canonicalToolName, cacheSettings, out var ttl) && !cacheBypassRequested)
                    {
                        if (result.Success || cacheSettings.CacheFailures)
                        {
                            cacheKey ??= ComputeStableCacheKey(canonicalToolName, immutableParameters);
                            var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
                            var json = TrySerializeToolResult(result);
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                await _cacheStore.UpsertAsync(new CachedToolResult
                                {
                                    ToolName = canonicalToolName,
                                    InputKey = cacheKey,
                                    ResultJson = json,
                                    ExpiresAt = expiresAt
                                }, context.CancellationToken).ConfigureAwait(false);

                                result.Metadata["cache_stored"] = true;
                                result.Metadata["cache_key"] = cacheKey;
                                result.Metadata["cache_expires_at_utc"] = expiresAt.ToString("O");
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Tool cache store failed for {ToolName}", canonicalToolName);
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw;
        }
        catch (TimeoutException ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(ex, "Tool {ToolName} exceeded runtime budget", canonicalToolName);

            TryAppendToolEvent(
                context,
                canonicalToolName,
                immutableParameters,
                success: false,
                duration: stopwatch.Elapsed,
                errorMessage: ex.Message);

            await RecordToolFailureDeadLetterAsync(
                context,
                canonicalToolName,
                immutableParameters,
                "tool_timeout",
                ex.Message,
                context.CancellationToken).ConfigureAwait(false);

            return new ToolResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message,
                Metadata = new Dictionary<string, object>
                {
                    ["resource_limit_timeout"] = true,
                    ["tool"] = canonicalToolName
                }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            if (_exceptionHandler != null)
            {
                var handlingResult = await _exceptionHandler.HandleExceptionAsync(
                    ex,
                    $"Tool_{canonicalToolName}_{context.AgentId}").ConfigureAwait(false);

                _logger.LogError(
                    ex,
                    "🔥 Tool {ToolName} failed | Category: {Category} | Retry: {ShouldRetry} | CorrelationId: {CorrelationId}",
                    canonicalToolName,
                    handlingResult.Category,
                    handlingResult.ShouldRetry,
                    handlingResult.CorrelationId);
            }
            else
            {
                _logger.LogError(ex, "Tool {ToolName} execution failed", canonicalToolName);
            }

            if (_learningService != null && context.AgentId != null)
            {
                _learningService.RecordToolExecution(
                    context.AgentId,
                    context.AgentRank ?? "Worker",
                    canonicalToolName,
                    success: false,
                    stopwatch.Elapsed);
            }

            TryAppendToolEvent(
                context,
                canonicalToolName,
                immutableParameters,
                success: false,
                duration: stopwatch.Elapsed,
                errorMessage: ex.Message);

            await RecordToolFailureDeadLetterAsync(
                context,
                canonicalToolName,
                immutableParameters,
                "tool_exception",
                ex.Message,
                context.CancellationToken).ConfigureAwait(false);

            return new ToolResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message
            };
        }
    }

    private async Task RecordToolFailureDeadLetterAsync(
        ToolExecutionContext context,
        string canonicalToolName,
        IReadOnlyDictionary<string, object> immutableParameters,
        string reasonCode,
        string error,
        CancellationToken ct)
    {
        if (_failedOperationStore is null)
        {
            return;
        }

        if (string.Equals(context.AgentId, FailedOperationReplayConstants.ReplayAgentId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = new ToolReplayPayload
        {
            ToolName = canonicalToolName,
            Parameters = CloneParameters(immutableParameters),
            AgentId = context.AgentId,
            AgentRank = context.AgentRank,
            AgentName = context.AgentName
        };

        try
        {
            await _failedOperationStore.RecordAsync(new FailedOperationRecord
            {
                Kind = FailedOperationKind.ToolExecution,
                ReasonCode = reasonCode,
                OperationName = canonicalToolName,
                AgentId = context.AgentId,
                TargetId = canonicalToolName,
                PayloadJson = JsonSerializer.Serialize(payload, JsonDefaults.Web),
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tool"] = canonicalToolName,
                    ["error"] = error
                }
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to record tool dead-letter for {ToolName}", canonicalToolName);
        }
    }

    private Task<ToolResult> ExecuteToolCoreAsync(ITool tool, Dictionary<string, object> parameters, CancellationToken ct)
    {
        if (_executionLimiter == null)
        {
            return tool.ExecuteAsync(parameters, ct);
        }

        return _executionLimiter.ExecuteAsync(innerCt => tool.ExecuteAsync(parameters, innerCt), ct);
    }

    private void TryAppendToolEvent(
        ToolExecutionContext context,
        string toolName,
        IReadOnlyDictionary<string, object> parameters,
        bool success,
        TimeSpan duration,
        string? errorMessage)
    {
        if (_eventSink == null || string.IsNullOrWhiteSpace(context.AgentId))
        {
            return;
        }

        var safeParametersJson = SafeSerialize(parameters);

        var evt = new AgentEvent
        {
            AgentId = context.AgentId,
            Type = success ? EventType.ToolExecuted : EventType.ErrorOccurred,
            Description = success
                ? $"Tool executed: {toolName}"
                : $"Tool failed: {toolName}",
            Metadata = new Dictionary<string, object>
            {
                ["tool"] = toolName,
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

    private static string SafeSerialize(IReadOnlyDictionary<string, object> parameters)
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

    private static bool IsCacheBypassRequested(IReadOnlyDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue("cache_bust", out var bust) && bust is bool b1 && b1)
        {
            return true;
        }

        if (parameters.TryGetValue("cache_skip", out var skip) && skip is bool b2 && b2)
        {
            return true;
        }

        return false;
    }

    private static bool TryResolveCachePolicy(string toolName, ToolResultCacheOptions options, out TimeSpan ttl)
    {
        ttl = options.DefaultTtl;

        if (!options.Enabled)
        {
            return false;
        }

        if (options.Tools.TryGetValue(toolName, out var overrideOptions))
        {
            if (overrideOptions.Volatile)
            {
                return false;
            }

            if (overrideOptions.Enabled == false)
            {
                return false;
            }

            if (overrideOptions.Ttl is not null)
            {
                ttl = overrideOptions.Ttl.Value;
            }

            return overrideOptions.Enabled == true || options.CacheableTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);
        }

        if (options.NonCacheableTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (options.CacheableTools.Count == 0)
        {
            return false;
        }

        return options.CacheableTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);
    }

    private static string ComputeStableCacheKey(string toolName, IReadOnlyDictionary<string, object> parameters)
    {
        var canonical = CanonicalizeParameters(parameters);
        var json = JsonSerializer.Serialize(canonical, JsonDefaults.Web);
        var material = $"{toolName}\n{json}";

        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static object CanonicalizeParameters(IReadOnlyDictionary<string, object> parameters)
    {
        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            sorted[key] = CanonicalizeValue(value);
        }

        return sorted;
    }

    private static object? CanonicalizeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement je)
        {
            return CanonicalizeJsonElement(je);
        }

        if (value is Dictionary<string, object> dict)
        {
            var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in dict)
            {
                sorted[k] = CanonicalizeValue(v);
            }

            return sorted;
        }

        if (value is IEnumerable<object> seq && value is not string)
        {
            return seq.Select(CanonicalizeValue).ToList();
        }

        return value;
    }

    private static Dictionary<string, object> CloneParameters(IReadOnlyDictionary<string, object> source)
    {
        return source.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    private static string ResolveCanonicalToolName(ToolExecutionContext context)
    {
        var preferred = string.IsNullOrWhiteSpace(context.Tool.Name)
            ? context.ToolName
            : context.Tool.Name;

        return NormalizeToolName(preferred);
    }

    private static string NormalizeToolName(string? toolName)
    {
        return (toolName ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static object? CanonicalizeJsonElement(JsonElement je)
    {
        return je.ValueKind switch
        {
            JsonValueKind.Object => je.EnumerateObject()
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(p => p.Name, p => CanonicalizeJsonElement(p.Value), StringComparer.Ordinal),
            JsonValueKind.Array => je.EnumerateArray().Select(CanonicalizeJsonElement).ToList(),
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => je.GetRawText()
        };
    }

    private static string? TrySerializeToolResult(ToolResult result)
    {
        try
        {
            return JsonSerializer.Serialize(result, JsonDefaults.Web);
        }
        catch
        {
            try
            {
                var sanitizedMetadata = result.Metadata.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)(kvp.Value?.ToString() ?? string.Empty));

                var sanitized = new ToolResult
                {
                    Success = result.Success,
                    Output = result.Output,
                    Error = result.Error,
                    Metadata = sanitizedMetadata
                };

                return JsonSerializer.Serialize(sanitized, JsonDefaults.Web);
            }
            catch
            {
                return null;
            }
        }
    }

    private static ToolResult? TryDeserializeToolResult(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ToolResult>(json, JsonDefaults.WebCaseInsensitive);
        }
        catch
        {
            return null;
        }
    }
}
