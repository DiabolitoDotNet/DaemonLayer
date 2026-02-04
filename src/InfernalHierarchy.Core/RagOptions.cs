namespace InfernalHierarchy.Core;

/// <summary>
/// Retrieval-Augmented Generation options.
/// </summary>
public sealed class RagOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxFacts { get; set; } = 6;
    public double MinScore { get; set; } = 0.70;
    public int MaxCharsPerFact { get; set; } = 320;
}
