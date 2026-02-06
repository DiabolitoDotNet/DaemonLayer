namespace InfernalHierarchy.Memory.Configuration;

public class MemoryPruningOptions
{
    public bool Enabled { get; set; }
    /// <summary>
    /// How often the pruning loop runs (in hours). Supports fractional values (e.g. 0.016 ≈ 1 minute).
    /// </summary>
    public double PruningIntervalHours { get; set; } = 24;

    /// <summary>
    /// When true, pruning computes and logs what it would remove, but performs no deletes/archives.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Safety cap to limit impact of a single run.
    /// </summary>
    public int MaxDeletesPerRun { get; set; } = 500;
    public int RetentionDays { get; set; } = 30;
    public double MinConfidenceThreshold { get; set; } = 0.3;
    public bool EnableArchival { get; set; }
    public string ArchivePath { get; set; } = "./archive/memory";

    /// <summary>
    /// How many recent decisions to consider for archival per run.
    /// </summary>
    public int DecisionsToScan { get; set; } = 1000;
}
