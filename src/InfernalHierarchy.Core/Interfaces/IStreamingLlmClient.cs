namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Optional LLM client capability: stream the completion text incrementally.
/// </summary>
public interface IStreamingLlmClient
{
    /// <summary>
    /// Gets a completion as a stream of text chunks.
    /// </summary>
    IAsyncEnumerable<string> GetStreamingCompletionAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);
}
