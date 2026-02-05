namespace InfernalHierarchy.Memory.Configuration;

public sealed class MemoryLearningOptions
{
    public bool Enabled { get; set; }

    public int IntervalMinutes { get; set; } = 60;

    public int MaxFactsPerRun { get; set; } = 50;

    public bool EnableCompression { get; set; } = true;
    public int CompressIfLongerThanChars { get; set; } = 1200;
    public int CompressToMaxChars { get; set; } = 500;

    public bool EnableClustering { get; set; } = true;
    public int MinClusterSize { get; set; } = 3;
    public double ClusterSimilarityThreshold { get; set; } = 0.86;

    public string SummaryCategory { get; set; } = "cluster_summary";
    public int SummaryMaxChars { get; set; } = 800;
}
