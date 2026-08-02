namespace InfernalHierarchy.Core.Entities;

public enum CapabilityOutcomeKind
{
    CustomToolCreated,
    CustomToolExecutionSucceeded,
    SkillPackGranted
}

public sealed class CapabilityOutcome
{
    public CapabilityOutcomeKind Kind { get; init; }

    public string CapabilityId { get; init; } = string.Empty;

    public string CapabilityType { get; init; } = string.Empty;

    public string SourceTask { get; init; } = string.Empty;

    public string RiskLevel { get; init; } = "Medium";

    public string AgentId { get; init; } = string.Empty;

    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
