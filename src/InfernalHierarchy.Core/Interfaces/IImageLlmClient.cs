namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Optional capability for LLM clients that support image-aware completions.
/// </summary>
public interface IImageLlmClient
{
    /// <summary>
    /// Gets a completion using a system prompt, user message, and one image.
    /// </summary>
    Task<string> GetImageCompletionAsync(
        string systemPrompt,
        string userMessage,
        byte[] imageBytes,
        string mimeType,
        string? modelOverride = null,
        CancellationToken ct = default);
}