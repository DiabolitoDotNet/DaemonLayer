using System.Collections.ObjectModel;

namespace InfernalHierarchy.Tools.Options;

public sealed class VoiceTranscriptionToolOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Path to a local transcription executable (e.g., whisper.cpp CLI).
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Working directory used for the process and temp files.
    /// If relative, resolved against current directory.
    /// </summary>
    public string RootDirectory { get; set; } = "data/voice";

    /// <summary>
    /// Arguments to pass to the executable. Use {input} placeholder for the audio path.
    /// </summary>
    public Collection<string> Arguments { get; } = new();

    public int TimeoutMs { get; set; } = 180_000;

    public int MaxOutputBytes { get; set; } = 64 * 1024;

    public long MaxInputBytes { get; set; } = 25 * 1024 * 1024;

    public Collection<string> AllowedExtensions { get; } = new() { ".ogg", ".wav", ".mp3", ".m4a", ".mp4" };
}
