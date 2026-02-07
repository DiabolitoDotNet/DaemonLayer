namespace InfernalHierarchy.Host.Configuration;

public sealed class VoiceCopilotOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of recent messages kept per session.
    /// This is total turns (user + assistant), not pairs.
    /// </summary>
    public int MaxHistoryMessages { get; set; } = 6;

    /// <summary>
    /// If true, the copilot will trigger TTS by default.
    /// </summary>
    public bool SpeakByDefault { get; set; } = true;

    public double Temperature { get; set; } = 0.4;

    /// <summary>
    /// Output token budget for low-latency voice replies.
    /// </summary>
    public int MaxTokens { get; set; } = 140;

    /// <summary>
    /// Hard cap on reply characters after post-processing.
    /// </summary>
    public int MaxReplyChars { get; set; } = 320;

    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// System prompt for the voice copilot.
    /// Keep it strict and short for low latency.
    /// </summary>
    public string SystemPrompt { get; set; } =
        "Tu es un assistant vocal en français. Réponds très brièvement (1-2 phrases), sans Markdown, et termine toujours par une question. " +
        "Sois concret et utile. Si l'entrée est incomplète, pose une question de clarification.";
}
