namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Abstraction for LLM chat completion clients (e.g., Ollama).
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Gets a completion using a system prompt and a user message.
    /// Implementations may apply model overrides, retries, and token accounting.
    /// </summary>
    /// <param name="systemPrompt">System prompt that defines behavior and constraints.</param>
    /// <param name="userMessage">User message / task input.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Convenience method for a single prompt completion.
    /// Use when you do not need a distinct system prompt.
    /// </summary>
    /// <param name="prompt">Prompt content.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default);
}
