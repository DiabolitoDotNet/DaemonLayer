namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Optional LLM client capability: allow overriding the model per request.
/// Useful for fine-tuned model variants (e.g., Ollama models created from LoRA/adapters).
/// </summary>
public interface IModelOverrideLlmClient
{
    Task<string> GetCompletionWithModelAsync(
        string systemPrompt,
        string userMessage,
        string model,
        CancellationToken ct = default);
}
