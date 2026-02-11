namespace InfernalHierarchy.Core.Configuration;

/// <summary>
/// Enables an optional self-reflection / critique loop.
///
/// Intended use: after a Prince/Supreme finishes a branch (i.e., produces a final report),
/// optionally run a dedicated Critic agent to review quality, flag contradictions,
/// and propose an improved synthesis.
///
/// This is designed to be lightweight: enabled only under simple heuristics and
/// uses a separate persona (default: Foras).
/// </summary>
public sealed class CritiqueOptions
{
    /// <summary>
    /// Enables the critique loop.
    /// Default is enabled, but the heuristics below keep it off for most shallow/cheap tasks.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Persona name for the critic (maps to <c>souls/{name}.json</c>).
    /// </summary>
    public string CriticPersonaName { get; set; } = "Foras";

    /// <summary>
    /// Rank assigned to the critic agent.
    /// </summary>
    public AgentRank CriticRank { get; set; } = AgentRank.Duke;

    /// <summary>
    /// Minimum inferred depth (root=1) to trigger critique.
    /// </summary>
    public int MinDepth { get; set; } = 3;

    /// <summary>
    /// Minimum number of tool calls (within the agent's own ReAct loop) to trigger critique.
    /// </summary>
    public int MinToolCalls { get; set; } = 5;

    /// <summary>
    /// Trigger critique when the user explicitly asks for verification.
    /// If any keyword is found (case-insensitive) in the original task content, critique is forced.
    /// </summary>
    public List<string> TriggerKeywords { get; set; } = new()
    {
        "vérifie",
        "verifie",
        "verify",
        "double-check",
        "check carefully",
        "validate",
        "assure-toi"
    };
}
