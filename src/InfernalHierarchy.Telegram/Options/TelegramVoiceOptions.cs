namespace InfernalHierarchy.Telegram.Options;

public sealed class TelegramVoiceOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// If true, agent reports will be sent as voice messages when possible.
    /// </summary>
    public bool ReplyWithVoice { get; set; } = false;

    public long MaxAudioBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Directory where Telegram voice files are downloaded before transcription.
    /// </summary>
    public string WorkingDirectory { get; set; } = "data/voice/telegram";

    public string TranscribeToolName { get; set; } = "audio_transcribe";

    public string SpeakToolName { get; set; } = "tts_speak";
}
