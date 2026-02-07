namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Represents an agent's persona (“soul”) loaded from JSON.
/// A persona defines the system prompt, specializations, tool availability, and behavioral traits.
/// </summary>
public class Persona
{
    /// <summary>
    /// Persona identifier/name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional themed title for the persona (used for display).
    /// </summary>
    public string DemonTitle { get; set; } = string.Empty;

    /// <summary>
    /// System prompt that defines behavior and constraints.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Optional model override (when multi-model support is enabled).
    /// </summary>
    public string? ModelOverride { get; set; }

    /// <summary>
    /// Specializations/tags describing what the persona is good at.
    /// </summary>
    public IReadOnlyList<string> Specializations { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Tool names the agent is allowed to request (subject to authorization policy).
    /// </summary>
    public IReadOnlyList<string> AvailableTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Behavioral traits such as tone and verbosity.
    /// </summary>
    public PersonalityTraits Personality { get; set; } = new();

    /// <summary>
    /// Additional persona-specific instructions.
    /// </summary>
    public Dictionary<string, string> CustomInstructions { get; init; } = new();
}

/// <summary>
/// Behavioral tuning settings for personas.
/// </summary>
public class PersonalityTraits
{
    /// <summary>
    /// Preferred response tone.
    /// </summary>
    public string Tone { get; set; } = "Professional";

    /// <summary>
    /// Preferred reasoning/working style.
    /// </summary>
    public string Approach { get; set; } = "Analytical";

    /// <summary>
    /// Verbosity target on a 1-10 scale.
    /// </summary>
    public int Verbosity { get; set; } = 5; // 1-10 scale

    /// <summary>
    /// Whether to apply the demonology-themed persona styling.
    /// </summary>
    public bool UseDemonicTheme { get; set; } = true;
}
