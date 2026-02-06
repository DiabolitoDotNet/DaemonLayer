namespace InfernalHierarchy.Host.Api;

public sealed record VoiceSpeakRequest(string Text);

public sealed record VoiceTranscribeResponse(
    string transcript,
    string tool,
    Dictionary<string, object> metadata);
