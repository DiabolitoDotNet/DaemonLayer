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

    public DefaultToolExecutionPipeline(
        ILogger<DefaultToolExecutionPipeline> logger,
        AgentLearningService? learningService = null,
        GlobalExceptionHandler? exceptionHandler = null,
        IAgentEventSink? eventSink = null,
        IToolRateLimiter? rateLimiter = null,
        IToolAuthorizationService? authorizationService = null,
        IToolResultCacheStore? cacheStore = null,
        IOptions<ToolResultCacheOptions>? cacheOptions = null)
    {
        _logger = logger;
        _learningService = learningService;
        _exceptionHandler = exceptionHandler;
        _eventSink = eventSink;
        _rateLimiter = rateLimiter;
        _authorizationService = authorizationService;
        _cacheStore = cacheStore;
        _cacheOptions = cacheOptions;
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

        // Cache lookup happens after authorization but before rate limiting.
        // This prevents expensive tools from being denied when a valid cached result exists.
        // If a tool is not cache-eligible, this is a no-op.
        if (_cacheStore != null && _cacheOptions?.Value.Enabled == true)
        {
            try
            {
                var cacheSettings = _cacheOptions.Value;
                if (TryResolveCachePolicy(context, cacheSettings, out var ttl) && !IsCacheBypassRequested(context.Parameters))
                {
                    var inputKey = ComputeStableCacheKey(context.ToolName, context.Parameters);
                    var cached = await _cacheStore.GetAsync(inputKey, context.CancellationToken).ConfigureAwait(false);
                    if (cached != null)
                    {
                        var cachedResult = TryDeserializeToolResult(cached.ResultJson);
                        if (cachedResult != null)
                        {
                            cachedResult.Metadata["cache_hit"] = true;
                            cachedResult.Metadata["cache_key"] = inputKey;
                            cachedResult.Metadata["cache_expires_at_utc"] = cached.ExpiresAt.ToString("O");
                            cachedResult.Metadata["cache_ttl_seconds"] = (long)ttl.TotalSeconds;
                            cachedResult.Metadata["tool"] = context.ToolName;

                            TryAppendToolEvent(
                                context,
                                success: cachedResult.Success,
                                duration: TimeSpan.Zero,
                                errorMessage: cachedResult.Success ? null : (cachedResult.Error ?? "Cached tool failure"));

                            return cachedResult;
                        }

                        // If we cannot deserialize, treat it as a miss and remove the bad entry.
                        await _cacheStore.RemoveAsync(inputKey, context.CancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Tool cache lookup failed for {ToolName}", context.ToolName);
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

            // Cache store happens after real execution.
            if (_cacheStore != null && _cacheOptions?.Value.Enabled == true)
            {
                try
                {
                    var cacheSettings = _cacheOptions.Value;
                    if (TryResolveCachePolicy(context, cacheSettings, out var ttl) && !IsCacheBypassRequested(context.Parameters))
                    {
                        if (result.Success || cacheSettings.CacheFailures)
                        {
                            var inputKey = ComputeStableCacheKey(context.ToolName, context.Parameters);
                            var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
                            var json = TrySerializeToolResult(result);
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                await _cacheStore.UpsertAsync(new CachedToolResult
                                {
                                    ToolName = context.ToolName,
                                    InputKey = inputKey,
                                    ResultJson = json,
                                    ExpiresAt = expiresAt
                                }, context.CancellationToken).ConfigureAwait(false);

                                result.Metadata["cache_stored"] = true;
                                result.Metadata["cache_key"] = inputKey;
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
                    _logger.LogDebug(ex, "Tool cache store failed for {ToolName}", context.ToolName);
                }
            }

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

    private static bool IsCacheBypassRequested(Dictionary<string, object> parameters)
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

    private static bool TryResolveCachePolicy(ToolExecutionContext context, ToolResultCacheOptions options, out TimeSpan ttl)
    {
        ttl = options.DefaultTtl;

        if (!options.Enabled)
        {
            return false;
        }

        var toolName = context.ToolName;

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

    private static string ComputeStableCacheKey(string toolName, Dictionary<string, object> parameters)
    {
        var canonical = CanonicalizeParameters(parameters);
        var json = JsonSerializer.Serialize(canonical, JsonDefaults.Web);
        var material = $"{toolName}\n{json}";

        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static object CanonicalizeParameters(Dictionary<string, object> parameters)
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
