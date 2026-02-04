using Microsoft.Extensions.Logging;
using Polly;

namespace InfernalHierarchy.Core;

/// <summary>
/// Extension methods for exception handling with retry logic
/// </summary>
public static class ExceptionHandlingExtensions
{
    /// <summary>
    /// Execute operation with automatic exception handling and retry
    /// </summary>
    public static async Task<T> ExecuteWithHandlingAsync<T>(
        this GlobalExceptionHandler handler,
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        int maxRetries = 3,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        correlationId ??= Guid.NewGuid().ToString();
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                return await operation(ct);
            }
            catch (Exception ex)
            {
                var result = await handler.HandleExceptionAsync(ex, operationName, correlationId, ct);

                // Don't retry if not transient or max retries exceeded
                if (!result.ShouldRetry || attempt >= maxRetries)
                {
                    throw;
                }

                // Exponential backoff: 1s, 2s, 4s
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>
    /// Execute void operation with automatic exception handling and retry
    /// </summary>
    public static async Task ExecuteWithHandlingAsync(
        this GlobalExceptionHandler handler,
        Func<CancellationToken, Task> operation,
        string operationName,
        int maxRetries = 3,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        await handler.ExecuteWithHandlingAsync(
            async ct =>
            {
                await operation(ct);
                return 0; // Dummy return value
            },
            operationName,
            maxRetries,
            correlationId,
            ct);
    }

    /// <summary>
    /// Create Polly retry policy from GlobalExceptionHandler
    /// </summary>
    public static IAsyncPolicy<T> CreateRetryPolicy<T>(
        this GlobalExceptionHandler handler,
        string operationName,
        int maxRetries = 3)
    {
        return Policy<T>
            .Handle<Exception>(ex => handler.ShouldRetryException(ex))
            .WaitAndRetryAsync(
                maxRetries,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: async (outcome, timespan, retryCount, context) =>
                {
                    var correlationId = context.CorrelationId != default
                        ? context.CorrelationId.ToString()
                        : Guid.NewGuid().ToString();
                    await handler.HandleExceptionAsync(
                        outcome.Exception,
                        operationName,
                        correlationId);
                });
    }

    /// <summary>
    /// Check if exception should be retried (internal helper)
    /// </summary>
    private static bool ShouldRetryException(this GlobalExceptionHandler handler, Exception exception)
    {
        // Simple check without async - categorize inline
        return exception switch
        {
            TimeoutException => true,
            HttpRequestException httpEx when httpEx.StatusCode != null
                && ((int)httpEx.StatusCode >= 500 || (int)httpEx.StatusCode == 429 || (int)httpEx.StatusCode == 408) => true,
            IOException => true,
            OperationCanceledException => false, // Don't retry cancellations
            _ => false
        };
    }
}
