namespace InfernalHierarchy.Host.Api;

public sealed record ChatRequest(string Message, string? ToAgentId = null, int? TimeoutMs = null);

public sealed record ChatResponse(
    string fromAgentId,
    string? toAgentId,
    string content,
    Dictionary<string, object> payload,
    DateTime receivedUtc,
    double durationMs);
