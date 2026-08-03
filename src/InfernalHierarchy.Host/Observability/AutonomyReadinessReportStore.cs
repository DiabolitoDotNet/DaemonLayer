namespace InfernalHierarchy.Host.Observability;

internal sealed record CapabilityReadinessItem(
    string Capability,
    bool Ready,
    bool ToolRegistered,
    bool ConfigurationReady,
    string Reason);

internal sealed record AutonomyReadinessReport(
    DateTime GeneratedAtUtc,
    bool AllCriticalReady,
    IReadOnlyList<CapabilityReadinessItem> Items);

internal sealed class AutonomyReadinessReportStore
{
    private readonly object _sync = new();
    private AutonomyReadinessReport _current = new(
        DateTime.UtcNow,
        AllCriticalReady: false,
        Items: Array.Empty<CapabilityReadinessItem>());

    public AutonomyReadinessReport GetCurrent()
    {
        lock (_sync)
        {
            return _current;
        }
    }

    public void Set(AutonomyReadinessReport report)
    {
        lock (_sync)
        {
            _current = report;
        }
    }
}
