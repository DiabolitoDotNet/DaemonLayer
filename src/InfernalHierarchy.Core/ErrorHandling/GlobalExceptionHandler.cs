using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace InfernalHierarchy.Core.ErrorHandling;

/// <summary>
/// Exception categories for error handling strategies
/// </summary>
public enum ExceptionCategory
{
    /// <summary>Transient errors that may succeed on retry (network, timeout)</summary>
    Transient,

    /// <summary>Business logic errors (validation, authorization)</summary>
    Business,

    /// <summary>System errors (out of memory, file not found)</summary>
    System,

    /// <summary>Unknown or unclassified errors</summary>
    Unknown
}

/// <summary>
/// Global exception handler with categorization and structured logging
/// </summary>
public class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly ActivitySource _activitySource;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
        _activitySource = new ActivitySource("InfernalHierarchy.ExceptionHandling");
    }

    /// <summary>
    /// Handle exception with categorization and correlation tracking
    /// </summary>
    public async Task<ExceptionHandlingResult> HandleExceptionAsync(
        Exception exception,
        string operationName,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        correlationId ??= Activity.Current?.Id ?? Guid.NewGuid().ToString();

        using var activity = _activitySource.StartActivity("HandleException");
        activity?.SetTag("operation", operationName);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("exception_type", exception.GetType().Name);

        var category = CategorizeException(exception);
        activity?.SetTag("exception_category", category.ToString());

        var shouldRetry = category == ExceptionCategory.Transient;
        var severity = GetSeverity(category);

        // Structured logging with all context
        _logger.Log(
            severity,
            exception,
            "Exception in {Operation}: {Message} | Category: {Category} | CorrelationId: {CorrelationId}",
            operationName,
            exception.Message,
            category,
            correlationId);

        // Record error in activity
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag("exception.type", exception.GetType().Name);
        activity?.SetTag("exception.message", exception.Message);

        // Allow async cleanup or notification hooks
        await OnExceptionHandledAsync(exception, category, correlationId, ct);

        return new ExceptionHandlingResult
        {
            Category = category,
            ShouldRetry = shouldRetry,
            CorrelationId = correlationId,
            Message = GetUserFriendlyMessage(exception, category),
            TechnicalDetails = exception.ToString()
        };
    }

    /// <summary>
    /// Categorize exception type for appropriate handling
    /// </summary>
    private ExceptionCategory CategorizeException(Exception exception)
    {
        return exception switch
        {
            // Transient errors
            TimeoutException => ExceptionCategory.Transient,
            HttpRequestException httpEx when IsTransientHttpError(httpEx) => ExceptionCategory.Transient,
            IOException ioEx when IsTransientIoError(ioEx) => ExceptionCategory.Transient,
            OperationCanceledException => ExceptionCategory.Transient,

            // Business errors
            ArgumentNullException => ExceptionCategory.Business,
            ArgumentException => ExceptionCategory.Business,
            InvalidOperationException => ExceptionCategory.Business,
            UnauthorizedAccessException => ExceptionCategory.Business,

            // System errors
            OutOfMemoryException => ExceptionCategory.System,
            StackOverflowException => ExceptionCategory.System,
            FileNotFoundException => ExceptionCategory.System,
            DirectoryNotFoundException => ExceptionCategory.System,

            // Aggregate exceptions - categorize by inner
            AggregateException aggEx when aggEx.InnerExceptions.Count == 1
                => CategorizeException(aggEx.InnerExceptions[0]),

            // Default
            _ => ExceptionCategory.Unknown
        };
    }

    /// <summary>
    /// Determine if HTTP error is transient (5xx, 429, 408)
    /// </summary>
    private bool IsTransientHttpError(HttpRequestException exception)
    {
        if (exception.StatusCode == null)
            return false;

        var statusCode = (int)exception.StatusCode;
        return statusCode >= 500 || statusCode == 429 || statusCode == 408;
    }

    /// <summary>
    /// Determine if IO error is transient (network share, temp unavailable)
    /// </summary>
    private bool IsTransientIoError(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode switch
        {
            0x20 => true,  // ERROR_SHARING_VIOLATION
            0x21 => true,  // ERROR_LOCK_VIOLATION
            0x27 => true,  // ERROR_HANDLE_EOF
            0x6D => true,  // ERROR_BAD_NETPATH
            _ => false
        };
    }

    /// <summary>
    /// Get log severity based on exception category
    /// </summary>
    private LogLevel GetSeverity(ExceptionCategory category)
    {
        return category switch
        {
            ExceptionCategory.Transient => LogLevel.Warning,
            ExceptionCategory.Business => LogLevel.Warning,
            ExceptionCategory.System => LogLevel.Error,
            ExceptionCategory.Unknown => LogLevel.Error,
            _ => LogLevel.Error
        };
    }

    /// <summary>
    /// Get user-friendly error message
    /// </summary>
    private string GetUserFriendlyMessage(Exception exception, ExceptionCategory category)
    {
        return category switch
        {
            ExceptionCategory.Transient => "The operation failed temporarily. It will be retried automatically.",
            ExceptionCategory.Business => $"Invalid operation: {exception.Message}",
            ExceptionCategory.System => "A system error occurred. Please contact support if the issue persists.",
            ExceptionCategory.Unknown => "An unexpected error occurred. Please try again later.",
            _ => exception.Message
        };
    }

    /// <summary>
    /// Hook for custom error handling logic (notifications, metrics, etc.)
    /// </summary>
    protected virtual Task OnExceptionHandledAsync(
        Exception exception,
        ExceptionCategory category,
        string correlationId,
        CancellationToken ct)
    {
        // Override in derived class for custom logic:
        // - Send alerts for System errors
        // - Track error metrics
        // - Create support tickets
        // - Notify administrators
        return Task.CompletedTask;
    }
}

/// <summary>
/// Result of exception handling with retry decision
/// </summary>
public class ExceptionHandlingResult
{
    /// <summary>Exception category</summary>
    public ExceptionCategory Category { get; set; }

    /// <summary>Whether operation should be retried</summary>
    public bool ShouldRetry { get; set; }

    /// <summary>Correlation ID for tracking</summary>
    public required string CorrelationId { get; set; }

    /// <summary>User-friendly error message</summary>
    public required string Message { get; set; }

    /// <summary>Technical details (for logging/debugging)</summary>
    public string? TechnicalDetails { get; set; }
}
