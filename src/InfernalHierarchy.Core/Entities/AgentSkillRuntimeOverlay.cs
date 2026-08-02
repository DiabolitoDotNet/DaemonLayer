namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Runtime overlay computed from active temporary skill grants.
/// </summary>
public sealed class AgentSkillRuntimeOverlay
{
    public IReadOnlyList<string> ActiveSkillPackIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AdditionalTools { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AdditionalSpecializations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PromptFragments { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Temporary runtime grant derived from an approved skill pack.
/// </summary>
public sealed class AgentSkillGrant
{
    public string SkillPackId { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }

    public IReadOnlyList<string> AdditionalTools { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AdditionalSpecializations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PromptFragments { get; init; } = Array.Empty<string>();
}
