namespace InfernalHierarchy.Agents.ReAct;

public interface IReActPromptBuilder
{
    string BuildPrompt(
        string systemContext,
        string conversationHistory,
        IReadOnlyCollection<string> availableTools,
        bool useJsonResponse);
}
