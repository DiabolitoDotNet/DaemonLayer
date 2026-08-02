namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Applies runtime resource constraints (concurrency/timeout budget) to tool execution.
/// </summary>
public interface IToolExecutionLimiter
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);
}