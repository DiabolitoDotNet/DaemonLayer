namespace InfernalHierarchy.Core.Configuration;

public sealed class FailedOperationHandlingOptions
{
    public bool Enabled { get; set; } = true;

    public int ReplayRetryBudget { get; set; } = 3;

    public int MaxEntries { get; set; } = 5000;
}
