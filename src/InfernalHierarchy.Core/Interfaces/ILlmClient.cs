namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Abstraction for LLM chat completion clients (e.g., Ollama).
/// </summary>
public interface ILlmClient
{
    Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default);

    Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default);
}
