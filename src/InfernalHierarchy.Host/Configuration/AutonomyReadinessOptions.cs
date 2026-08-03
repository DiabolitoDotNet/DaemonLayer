namespace InfernalHierarchy.Host.Configuration;
using System.Diagnostics.CodeAnalysis;

public sealed class AutonomyReadinessOptions
{
    public bool Enabled { get; set; } = true;
    public bool FailStartupOnCriticalNotReady { get; set; } = false;

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Array binding keeps configuration concise and this options model is not used as a mutable hot-path collection.")]
    public string[] CriticalCapabilities { get; set; } = ["email_inbox_query"];
}
