namespace InfernalHierarchy.Core.Results;

/// <summary>
/// Lightweight success/failure result type for boundary-safe APIs.
/// Prefer returning <see cref="Result{T}"/> for expected failures instead of throwing.
/// </summary>
public static class Result
{
    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);

    public static Result<T> Fail<T>(string message, string? code = null, Exception? exception = null) =>
        Result<T>.Fail(message, code, exception);
}

/// <summary>
/// Represents a success/failure outcome with an optional value and error details.
/// </summary>
public readonly record struct Result<T>
{
    private Result(bool succeeded, T? value, ResultError? error)
    {
        Succeeded = succeeded;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// True when the operation completed successfully.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// The successful value. Only meaningful when <see cref="Succeeded"/> is true.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error details. Only meaningful when <see cref="Succeeded"/> is false.
    /// </summary>
    public ResultError? Error { get; }

    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(string message, string? code = null, Exception? exception = null) =>
        new(false, default, new ResultError(message, code, exception));

    /// <summary>
    /// Returns the value when succeeded; otherwise throws an <see cref="InvalidOperationException"/>.
    /// Use this only at boundaries where exceptions are acceptable.
    /// </summary>
    public T GetValueOrThrow()
    {
        if (Succeeded && Value is not null)
        {
            return Value;
        }

        var message = Error?.Message ?? "Operation failed";
        throw new InvalidOperationException(message, Error?.Exception);
    }
}

/// <summary>
/// Error details for a failed <see cref="Result{T}"/>.
/// </summary>
public sealed record ResultError(string Message, string? Code = null, Exception? Exception = null);
