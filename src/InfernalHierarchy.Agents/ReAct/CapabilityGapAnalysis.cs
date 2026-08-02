namespace InfernalHierarchy.Agents.ReAct;

public enum CapabilityRemediationActionKind
{
    CreateCustomTool,
    RequestSkillPack,
    EscalateCollaboration,
    SwitchExecutionProfile
}

public sealed record CapabilityGap(
    string Capability,
    string ReasonCode,
    string Description,
    bool BlockedByProfile,
    string? SuggestedSkillPackId,
    string? SuggestedExecutionProfile);

public sealed record CapabilityRemediationAction(
    CapabilityRemediationActionKind Kind,
    string ReasonCode,
    string Capability,
    string Description,
    string? SkillPackId = null,
    string? TargetExecutionProfile = null,
    string? CustomToolName = null,
    string? CustomToolRequirement = null);

public sealed record CapabilityGapAnalysisResult(
    IReadOnlyList<CapabilityGap> Gaps,
    IReadOnlyList<CapabilityRemediationAction> Remediations)
{
    public bool HasGaps => Gaps.Count > 0;
}

public sealed record CapabilityRemediationExecutionResult(
    IReadOnlyList<CapabilityRemediationAction> AppliedActions,
    IReadOnlyList<CapabilityRemediationAction> FailedActions,
    IReadOnlyList<string> NewlyAvailableTools,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded => FailedActions.Count == 0;
}

public interface ICapabilityGapAnalyzer
{
    Task<CapabilityGapAnalysisResult> AnalyzeAsync(
        ReActTaskProcessorContext context,
        AgentMessage task,
        Persona effectivePersona,
        CancellationToken ct);
}

public interface ICapabilityRemediationOrchestrator
{
    Task<CapabilityRemediationExecutionResult> ExecuteAsync(
        ReActTaskProcessorContext context,
        AgentMessage task,
        CapabilityGapAnalysisResult analysis,
        CancellationToken ct);
}
