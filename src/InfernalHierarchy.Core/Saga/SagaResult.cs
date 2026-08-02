namespace InfernalHierarchy.Core.Saga;

/// <summary>
/// Result of saga execution
/// </summary>
public class SagaResult
{
    /// <summary>
    /// Gets or sets whether saga succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the saga context
    /// </summary>
    public SagaContext Context { get; set; } = new();

    /// <summary>
    /// Gets or sets error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets whether compensation was successful
    /// </summary>
    public bool? CompensationSuccess { get; set; }

    /// <summary>
    /// Gets or sets total execution time
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }

    /// <summary>
    /// Gets or sets structured reason code when saga fails.
    /// </summary>
    public string FailureReasonCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets suggested next autonomous action when saga fails.
    /// </summary>
    public string NextAction { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether supervisor escalation should be requested.
    /// </summary>
    public bool NeedsSupervisorIntervention { get; set; }
}
