namespace InfernalHierarchy.Host.Observability;

internal sealed class PerfMiniProfilerOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Base path for MiniProfiler routes.
    /// </summary>
    public string RouteBasePath { get; set; } = "/mini-profiler-resources";

    /// <summary>
    /// How long to retain profiling results in memory.
    /// </summary>
    public int StorageMinutes { get; set; } = 30;
}
