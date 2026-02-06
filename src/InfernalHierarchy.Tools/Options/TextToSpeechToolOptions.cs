using System.Collections.ObjectModel;

namespace InfernalHierarchy.Tools.Options;

public sealed class TextToSpeechToolOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// If true, synthesize in-process using a Piper/VITS ONNX voice via LMSupply.Synthesizer (CPU-only).
    /// When enabled, <see cref="ExecutablePath"/> and <see cref="Arguments"/> are ignored.
    /// </summary>
    public bool UsePiperNet { get; set; } = false;

    /// <summary>
    /// Piper voice directory (recommended) or model alias understood by the synthesizer.
    /// Typically contains the ONNX model + config/json alongside it.
    /// </summary>
    public string PiperVoicePath { get; set; } = string.Empty;

    /// <summary>
    /// Speaker id for multi-speaker voices (usually 0 for single-speaker).
    /// </summary>
    public int PiperSpeakerId { get; set; } = 0;

    /// <summary>
    /// Speech speed (1.0 = normal). Values outside a sane range are clamped by the tool.
    /// </summary>
    public float PiperSpeed { get; set; } = 1.0f;

    /// <summary>
    /// Thread count for synthesis (0 = auto).
    /// </summary>
    public int PiperThreadCount { get; set; } = 0;

    /// <summary>
    /// Path to a local TTS executable.
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Root directory where output audio files are written.
    /// If relative, resolved against current directory.
    /// </summary>
    public string RootDirectory { get; set; } = "data/voice";

    /// <summary>
    /// Arguments to pass to the executable. Use {text} and {output} placeholders.
    /// </summary>
    public Collection<string> Arguments { get; } = new();

    public int TimeoutMs { get; set; } = 180_000;

    public int MaxOutputBytes { get; set; } = 64 * 1024;

    public int MaxTextChars { get; set; } = 5_000;

    public string OutputExtension { get; set; } = ".wav";
}
