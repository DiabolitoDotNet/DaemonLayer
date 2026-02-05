using System.Collections.ObjectModel;

namespace InfernalHierarchy.Core.Saga;

/// <summary>
/// Saga coordinator for distributed transactions
/// </summary>
public interface ISaga
{
    /// <summary>
    /// Gets the saga identifier
    /// </summary>
    string SagaId { get; }

    /// <summary>
    /// Gets the saga name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the saga steps
    /// </summary>
    Collection<ISagaStep> Steps { get; }

    /// <summary>
    /// Executes the saga
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Saga execution result</returns>
    Task<SagaResult> ExecuteAsync(CancellationToken ct = default);
}
