namespace InfernalHierarchy.Host.Observability;

public sealed class SloGateOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum allowed growth of pending dead-letters since process startup.
    /// </summary>
    public int MaxDeadLetterBacklogGrowth { get; set; } = 25;

    /// <summary>
    /// Minimum acceptable replay success ratio once enough samples exist.
    /// </summary>
    public double MinReplaySuccessRatio { get; set; } = 0.90;

    /// <summary>
    /// Minimum replay attempts required before enforcing replay ratio gate.
    /// </summary>
    public int MinReplaySamples { get; set; } = 5;

    /// <summary>
    /// Maximum acceptable queue reject rate.
    /// </summary>
    public double MaxQueueRejectRate { get; set; } = 0.02;

    /// <summary>
    /// Minimum published messages required before enforcing reject-rate gate.
    /// </summary>
    public int MinQueueSamples { get; set; } = 20;

    /// <summary>
    /// Maximum acceptable p95 task completion latency for /api/chat requests.
    /// </summary>
    public double MaxTaskCompletionP95Ms { get; set; } = 15000;

    /// <summary>
    /// Minimum /api/chat latency samples required before enforcing latency gate.
    /// </summary>
    public int MinTaskCompletionSamples { get; set; } = 5;
}

public sealed record SloGateCheckResult(
    string Gate,
    bool Passed,
    string Status,
    double Value,
    double Threshold,
    string Unit,
    string Message);

public sealed record SloGateEvaluationResult(
    bool Passed,
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<SloGateCheckResult> Checks);