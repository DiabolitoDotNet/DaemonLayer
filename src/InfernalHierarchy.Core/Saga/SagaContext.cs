using System.Collections.ObjectModel;

namespace InfernalHierarchy.Core.Saga;

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
    public Dictionary<string, object> Data { get; } = new();

    /// <summary>
    /// Gets or sets completed step names
    /// </summary>
    public Collection<string> CompletedSteps { get; } = new();

    /// <summary>
    /// Gets or sets compensated step names
    /// </summary>
    public Collection<string> CompensatedSteps { get; } = new();

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
    public int CurrentStep { get; set; }

    /// <summary>
    /// Gets or sets error information if saga failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}
