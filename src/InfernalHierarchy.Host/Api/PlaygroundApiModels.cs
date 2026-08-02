namespace InfernalHierarchy.Host.Api;

public sealed record PlaygroundScenarioCreateRequest(
    string Name,
    string Prompt,
    string? ToAgentId = null,
    int? TimeoutMs = null,
    Dictionary<string, object>? Tags = null);

public sealed record PlaygroundScenarioRunRequest(
    string? Prompt = null,
    string? ToAgentId = null,
    int? TimeoutMs = null);