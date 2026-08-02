namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Reusable capability bundle that can be attached to agents.
/// </summary>
public sealed class SkillPack
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = "1.0.0";

    public string Description { get; init; } = string.Empty;

    public string RiskLevel { get; init; } = "Low";

    public bool Enabled { get; init; } = true;

    public int Priority { get; init; } = 100;

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedRanks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AdditionalTools { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AdditionalSpecializations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PromptFragments { get; init; } = Array.Empty<string>();

    public Dictionary<string, string> CustomInstructions { get; init; } = new();
}
