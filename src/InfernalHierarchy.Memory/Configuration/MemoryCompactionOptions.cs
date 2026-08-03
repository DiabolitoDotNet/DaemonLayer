namespace InfernalHierarchy.Memory.Configuration;

public sealed class MemoryCompactionOptions
{
    public bool Enabled { get; set; }

    public bool RunOnStartup { get; set; } = false;

    public double IntervalHours { get; set; } = 24;

    public long MinDatabaseSizeBytes { get; set; } = 128L * 1024L * 1024L;

    public bool IncludeErrorReport { get; set; } = false;
}