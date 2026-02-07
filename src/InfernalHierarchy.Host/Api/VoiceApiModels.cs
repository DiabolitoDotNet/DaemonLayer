namespace InfernalHierarchy.Host.Api;

public sealed record VoiceSpeakRequest(string Text);

public sealed record VoiceCopilotRequest(
    string Text,
    string? SessionId = null,
    bool? Speak = null);

public sealed record VoiceTranscribeResponse(
    string transcript,
    string tool,
    Dictionary<string, object> metadata);

public sealed record VoiceCopilotResponse(
    string sessionId,
    string reply,
    string speechText,
    bool ttsEnqueued);
