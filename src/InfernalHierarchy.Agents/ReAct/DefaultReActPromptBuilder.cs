using System.Text;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultReActPromptBuilder : IReActPromptBuilder
{
    public string BuildPrompt(
        string systemContext,
        string conversationHistory,
        IReadOnlyCollection<string> availableTools,
        bool useJsonResponse)
    {
        if (useJsonResponse)
        {
            return $$"""
                        {{systemContext}}

                        # Conversation History
                        {{conversationHistory}}

                        # Instructions
                        Follow the ReAct pattern:
                        1. Think about what you need to do next
                        2. Choose a tool to use (or FINAL_ANSWER if done)

                        Respond with a SINGLE JSON object and nothing else (no Markdown, no code fences).
                        Required properties:
                        - thought: string
                        - action: string (tool name or FINAL_ANSWER)
                        - actionInput: object (tool parameters) OR string (final answer)

                        Example tool call:
                        {\"thought\":\"I should search memory\",\"action\":\"memory_search\",\"actionInput\":{\"query\":\"...\"} }

                        Example final answer:
                        {\"thought\":\"I am done\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"<final answer text>\"}

                        Available tools: {{string.Join(", ", availableTools)}}
                        """;
        }

        return $"""
                        {systemContext}

                        # Conversation History
                        {conversationHistory}

                        # Instructions
                        Follow the ReAct pattern:
                        1. Thought: Analyze what you need to do next
                        2. Action: Choose a tool to use (or FINAL_ANSWER if done)
                        3. Provide your response in this exact format:

                        Thought: <your reasoning>
                        Action: <tool_name or FINAL_ANSWER>
                        Action Input: <tool parameters as JSON or final answer text>

                        Available tools: {string.Join(", ", availableTools)}
                        """;
    }
}
