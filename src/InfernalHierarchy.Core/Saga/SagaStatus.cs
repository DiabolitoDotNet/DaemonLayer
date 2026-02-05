namespace InfernalHierarchy.Core.Saga;

/// <summary>
/// Status of saga execution
/// </summary>
public enum SagaStatus
{
    /// <summary>
    /// Saga not started
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Saga is executing
    /// </summary>
    Running = 1,

    /// <summary>
    /// Saga completed successfully
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Saga failed and is compensating
    /// </summary>
    Compensating = 3,

    /// <summary>
    /// Saga compensation completed
    /// </summary>
    Compensated = 4,

    /// <summary>
    /// Saga failed and compensation failed
    /// </summary>
    Failed = 5
}
