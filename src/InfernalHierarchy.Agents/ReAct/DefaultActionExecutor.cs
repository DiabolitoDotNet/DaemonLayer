using System.Text.Json;
using InfernalHierarchy.Core.Interfaces;

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
            toolName.Contains("agent", StringComparison.OrdinalIgnoreCase))
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

        var toolObservation = toolResult.Success
            ? $"Observation: {toolResult.Output}"
            : $"Observation: Tool execution failed - {toolResult.Error}";

        return new ActionExecutionResult(
            ToolFound: true,
            Success: toolResult.Success,
            Observation: toolObservation,
            ToolCall: toolCall,
            Error: toolResult.Error);
    }
}
