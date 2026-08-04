namespace InfernalHierarchy.Host.Api;

public sealed record ChatRequest(
    string Message,
    string? ToAgentId = null,
    int? TimeoutMs = null,
    string? ExecutionProfile = null,
    long? TelegramChatId = null);

public sealed record ChatResponse(
    string fromAgentId,
    string? toAgentId,
    string content,
    Dictionary<string, object> payload,
    string? correlationId,
    string? causationId,
    DateTime receivedUtc,
    double durationMs);
