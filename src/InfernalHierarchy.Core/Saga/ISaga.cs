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
    List<ISagaStep> Steps { get; }

    /// <summary>
    /// Executes the saga
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Saga execution result</returns>
    Task<SagaResult> ExecuteAsync(CancellationToken ct = default);
}

/// <summary>
/// Context passed between saga steps
/// </summary>
public class SagaContext
{
    /// <summary>
    /// Gets or sets the saga identifier
    /// </summary>
    public string SagaId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets shared data between steps
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// Gets or sets completed step names
    /// </summary>
    public List<string> CompletedSteps { get; set; } = new();

    /// <summary>
    /// Gets or sets compensated step names
    /// </summary>
    public List<string> CompensatedSteps { get; set; } = new();

    /// <summary>
    /// Gets or sets saga start time
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets saga end time
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets or sets current step index
    /// </summary>
    public int CurrentStep { get; set; } = 0;

    /// <summary>
    /// Gets or sets error information if saga failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

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
}

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
