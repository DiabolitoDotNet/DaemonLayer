using System.Text.Json;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultActionExecutor : IActionExecutor
{
    private readonly IActionInputParser _inputParser;

    public DefaultActionExecutor(IActionInputParser inputParser)
    {
        _inputParser = inputParser;
    }

    public async Task<ActionExecutionResult> ExecuteAsync(ActionExecutionContext context)
    {
        var toolName = context.ToolName.Trim();

        if (!context.AvailableTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            var availableTools = string.Join(", ", context.AvailableTools);
            return new ActionExecutionResult(
                ToolFound: false,
                Success: false,
                Observation: $"Observation: Tool '{toolName}' is not allowed. Available tools: {availableTools}",
                ToolCall: null,
                Error: "Tool not allowed");
        }

        var tool = context.ToolRegistry.GetTool(toolName);

        if (tool == null)
        {
            var availableTools = string.Join(", ", context.AvailableTools);
            var missingToolObservation = $"Observation: Tool '{context.ToolName}' not found. Available tools: {availableTools}";
            return new ActionExecutionResult(
                ToolFound: false,
                Success: false,
                Observation: missingToolObservation,
                ToolCall: null,
                Error: "Tool not found");
        }

        Dictionary<string, object> parameters;
        if (context.ActionInputObject != null)
        {
            parameters = context.ActionInputObject;
        }
        else
        {
            parameters = _inputParser.Parse(context.ActionInputText, toolName);
        }

        if (toolName.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("agent", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("skill", StringComparison.OrdinalIgnoreCase))
        {
            parameters["agent_id"] = context.AgentId;
            parameters["agent_rank"] = context.AgentRank;
            parameters["parent_agent_id"] = context.AgentId;
        }

        var toolResult = await context.ToolRegistry.ExecuteToolWithTrackingAsync(
            toolName,
            parameters,
            context.AgentId,
            context.AgentRank,
            context.AgentName,
            context.CancellationToken).ConfigureAwait(false);

        string toolCall;
        if (context.ActionInputObject != null)
        {
            toolCall = $"{toolName}({JsonSerializer.Serialize(context.ActionInputObject)})";
        }
        else
        {
            toolCall = $"{toolName}({context.ActionInputText})";
        }

        static string Truncate(string? value, int maxLen)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length <= maxLen) return value;
            return value.Substring(0, maxLen) + "\n…(truncated)";
        }

        var toolObservation = toolResult.Success
            ? $"Observation: {Truncate(toolResult.Output, 4000)}"
            : BuildFailureObservation(toolResult);

        static string BuildFailureObservation(ToolResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Observation: Tool execution failed - ");
            sb.Append(result.Error);

            var details = Truncate(result.Output, 6000);
            if (!string.IsNullOrWhiteSpace(details))
            {
                sb.Append("\nDetails:\n");
                sb.Append(details);
            }

            return sb.ToString();
        }

        return new ActionExecutionResult(
            ToolFound: true,
            Success: toolResult.Success,
            Observation: toolObservation,
            ToolCall: toolCall,
            Error: toolResult.Error);
    }
}
