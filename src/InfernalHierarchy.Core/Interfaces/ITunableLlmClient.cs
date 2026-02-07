namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Optional LLM client capability: allow per-call tuning (e.g., token budget, temperature).
/// </summary>
public interface ITunableLlmClient
{
    Task<string> GetCompletionWithOptionsAsync(
        string systemPrompt,
        string userMessage,
        double? temperature,
        int? maxTokens,
        CancellationToken ct = default);

    IAsyncEnumerable<string> GetStreamingCompletionWithOptionsAsync(
        string systemPrompt,
        string userMessage,
        double? temperature,
        int? maxTokens,
        CancellationToken ct = default);
}
