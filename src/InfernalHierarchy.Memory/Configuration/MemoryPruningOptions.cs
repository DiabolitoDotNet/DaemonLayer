namespace InfernalHierarchy.Memory.Configuration;

public class MemoryPruningOptions
{
    public bool Enabled { get; set; }
    public int PruningIntervalHours { get; set; } = 24;
    public int RetentionDays { get; set; } = 30;
    public double MinConfidenceThreshold { get; set; } = 0.3;
    public bool EnableArchival { get; set; }
    public string ArchivePath { get; set; } = "./archive/memory";
}
