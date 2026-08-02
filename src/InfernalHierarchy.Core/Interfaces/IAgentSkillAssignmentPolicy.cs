namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Governs baseline and temporary skill assignment for agents.
/// </summary>
public interface IAgentSkillAssignmentPolicy
{
    Task<IReadOnlyList<string>> SelectInitialSkillPackIdsAsync(
        Persona persona,
        AgentRank targetRank,
        string? parentAgentId,
        CancellationToken ct = default);

    Task<SkillAssignmentDecision> EvaluateTemporarySkillRequestAsync(
        SkillAssignmentRequest request,
        CancellationToken ct = default);
}
