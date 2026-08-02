namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Optional LLM client capability: route model selection per request using task hints.
/// </summary>
public interface IModelRoutingLlmClient
{
    Task<string> GetCompletionWithRoutingAsync(
        string systemPrompt,
        string userMessage,
        LlmRoutingHint routingHint,
        double? temperature = null,
        int? maxTokens = null,
        CancellationToken ct = default);

    IAsyncEnumerable<string> GetStreamingCompletionWithRoutingAsync(
        string systemPrompt,
        string userMessage,
        LlmRoutingHint routingHint,
        double? temperature = null,
        int? maxTokens = null,
        CancellationToken ct = default);
}

public sealed class LlmRoutingHint
{
    /// <summary>
    /// Logical task family (examples: voice, coding, chat, retrieval).
    /// </summary>
    public string TaskType { get; init; } = string.Empty;

    /// <summary>
    /// Optional latency budget for this call.
    /// </summary>
    public int? LatencyBudgetMs { get; init; }
}