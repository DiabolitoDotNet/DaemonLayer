namespace InfernalHierarchy.Core.Saga;

/// <summary>
/// Represents a step in a saga with compensation logic
/// </summary>
public interface ISagaStep
{
    /// <summary>
    /// Gets the step name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the step
    /// </summary>
    /// <param name="context">Saga context</param>
    /// <param name="ct">Cancellation token</param>
    Task ExecuteAsync(SagaContext context, CancellationToken ct = default);

    /// <summary>
    /// Compensates (undoes) the step if saga fails
    /// </summary>
    /// <param name="context">Saga context</param>
    /// <param name="ct">Cancellation token</param>
    Task CompensateAsync(SagaContext context, CancellationToken ct = default);
}
