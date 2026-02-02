namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Represents an agent's persona/soul loaded from JSON
/// </summary>
public class Persona
{
    public string Name { get; set; } = string.Empty;
    public string DemonTitle { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public IReadOnlyList<string> Specializations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AvailableTools { get; init; } = Array.Empty<string>();
    public PersonalityTraits Personality { get; set; } = new();
    public Dictionary<string, string> CustomInstructions { get; init; } = new();
}

public class PersonalityTraits
{
    public string Tone { get; set; } = "Professional";
    public string Approach { get; set; } = "Analytical";
    public int Verbosity { get; set; } = 5; // 1-10 scale
    public bool UseDemonicTheme { get; set; } = true;
}
