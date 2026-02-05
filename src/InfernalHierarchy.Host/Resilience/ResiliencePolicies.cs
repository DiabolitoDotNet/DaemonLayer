using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Resilience;

/// <summary>
/// Circuit breaker and retry policies for resilience
/// </summary>
public class ResiliencePolicies
{
    private readonly ILogger<ResiliencePolicies> _logger;

    public ResiliencePolicies(ILogger<ResiliencePolicies> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// HTTP request policy with retry and circuit breaker
    /// </summary>
    public IAsyncPolicy<HttpResponseMessage> HttpRequestPolicy { get; private set; } = Policy.NoOpAsync<HttpResponseMessage>();

    /// <summary>
    /// LLM call policy with retry
    /// </summary>
    public IAsyncPolicy LlmCallPolicy { get; private set; } = Policy.NoOpAsync();

    /// <summary>
    /// Database operation policy with retry
    /// </summary>
    public IAsyncPolicy DatabasePolicy { get; private set; } = Policy.NoOpAsync();

    /// <summary>
    /// Tool execution policy
    /// </summary>
    public IAsyncPolicy ToolExecutionPolicy { get; private set; } = Policy.NoOpAsync();

    public void Initialize()
    {
        // HTTP request policy: 3 retries + circuit breaker
        var httpRetry = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "HTTP request retry {RetryCount}: Status={Status}, Waiting {Delay}ms",
                        retryCount, outcome.Result?.StatusCode, timespan.TotalMilliseconds);
                });

        var httpCircuitBreaker = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => !r.IsSuccessStatusCode)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromMinutes(1),
                onBreak: (outcome, duration) =>
                {
                    _logger.LogError("Circuit breaker opened for {Duration}s due to repeated failures", duration.TotalSeconds);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker half-open, testing...");
                });

        HttpRequestPolicy = Policy.WrapAsync(httpRetry, httpCircuitBreaker);

        // LLM call policy: 2 retries with exponential backoff
        LlmCallPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(exception,
                        "LLM call retry {RetryCount}: Waiting {Delay}ms",
                        retryCount, timespan.TotalMilliseconds);
                });

        // Database policy: 3 retries with shorter delays
        DatabasePolicy = Policy
            .Handle<Exception>(ex => !(ex is OperationCanceledException))
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(100 * retryAttempt),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(exception,
                        "Database operation retry {RetryCount}: Waiting {Delay}ms",
                        retryCount, timespan.TotalMilliseconds);
                });

        // Tool execution policy: 2 retries
        ToolExecutionPolicy = Policy
            .Handle<Exception>(ex => !(ex is OperationCanceledException) && !(ex is ArgumentException))
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(retryAttempt),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(exception,
                        "Tool execution retry {RetryCount}: Waiting {Delay}ms",
                        retryCount, timespan.TotalMilliseconds);
                });
    }
}

/// <summary>
/// Service to access resilience policies
/// </summary>
public interface IResiliencePolicyProvider
{
    IAsyncPolicy<HttpResponseMessage> GetHttpPolicy();
    IAsyncPolicy GetLlmPolicy();
    IAsyncPolicy GetDatabasePolicy();
    IAsyncPolicy GetToolExecutionPolicy();
}

public class ResiliencePolicyProvider : IResiliencePolicyProvider
{
    private readonly ResiliencePolicies _policies;

    public ResiliencePolicyProvider(ResiliencePolicies policies)
    {
        _policies = policies;
        _policies.Initialize();
    }

    public IAsyncPolicy<HttpResponseMessage> GetHttpPolicy() => _policies.HttpRequestPolicy;
    public IAsyncPolicy GetLlmPolicy() => _policies.LlmCallPolicy;
    public IAsyncPolicy GetDatabasePolicy() => _policies.DatabasePolicy;
    public IAsyncPolicy GetToolExecutionPolicy() => _policies.ToolExecutionPolicy;
}
