using System.Text;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultReActLoopRunner : IReActLoopRunner
{
    private const int MaxIterations = 5;

    public async Task<ReActLoopResult> RunAsync(ReActLoopContext context, CancellationToken ct)
    {
        var history = new StringBuilder();
        var toolCalls = new List<string>();
        var iterations = 0;
        var consecutiveParseFailures = 0;
        const int maxParseFailures = 3;

        history.AppendLine($"Task: {context.Task}\n");

        while (iterations < MaxIterations)
        {
            iterations++;
            context.SetStatus(AgentStatus.Thinking);

            try
            {
                // Build prompt with history
                var prompt = context.PromptBuilder.BuildPrompt(
                    context.SystemContext,
                    history.ToString(),
                    context.Persona.AvailableTools,
                    context.ReActOptions.UseJsonResponse);

                context.Logger.LogDebug("Iteration {Iteration}: Calling LLM", iterations);

                var response = context.LlmClient is IModelOverrideLlmClient modelOverrideClient
                    && !string.IsNullOrWhiteSpace(context.Persona.ModelOverride)
                        ? await modelOverrideClient.GetCompletionWithModelAsync(
                            context.Persona.SystemPrompt,
                            prompt,
                            context.Persona.ModelOverride!,
                            ct)
                        : await context.LlmClient.GetCompletionAsync(
                            context.Persona.SystemPrompt,
                            prompt,
                            ct);

                if (string.IsNullOrWhiteSpace(response))
                {
                    context.Logger.LogWarning("LLM returned empty response");
                    history.AppendLine("Observation: LLM returned empty response. Retrying...");
                    continue;
                }

                history.AppendLine($"\n--- Iteration {iterations} ---");
                history.AppendLine(response);

                if (!context.ActionParser.TryParse(response, context.ReActOptions.UseJsonResponse, out var parsed))
                {
                    consecutiveParseFailures++;
                    context.Logger.LogWarning(
                        "Failed to parse action from response (failure {Count}/{Max})",
                        consecutiveParseFailures,
                        maxParseFailures);

                    if (consecutiveParseFailures >= maxParseFailures)
                    {
                        return new ReActLoopResult(
                            FinalAnswer: "Unable to complete task due to repeated parsing failures.",
                            Reasoning: "LLM responses did not follow expected format",
                            Iterations: iterations,
                            ToolCalls: toolCalls);
                    }

                    history.AppendLine(
                        "Observation: Response format incorrect. Please follow the Thought/Action/Action Input format exactly.");
                    continue;
                }

                var thought = parsed.Thought;
                var action = parsed.Action;
                var actionInput = parsed.ActionInputText;
                var actionInputObject = parsed.ActionInputObject;

                context.Logger.LogInformation("💭 Thought: {Thought}", thought);
                context.Logger.LogInformation("⚡ Action: {Action}", action);

                consecutiveParseFailures = 0;

                if (action.Contains("FINAL_ANSWER", StringComparison.OrdinalIgnoreCase))
                {
                    return new ReActLoopResult(
                        FinalAnswer: actionInput,
                        Reasoning: thought,
                        Iterations: iterations,
                        ToolCalls: toolCalls);
                }

                try
                {
                    context.SetStatus(AgentStatus.ActingWithTool);

                    var exec = await context.ActionExecutor.ExecuteAsync(new ActionExecutionContext(
                        ToolRegistry: context.ToolRegistry,
                        ToolName: action,
                        ActionInputText: actionInput,
                        ActionInputObject: actionInputObject,
                        AgentId: context.AgentId,
                        AgentName: context.AgentName,
                        AgentRank: context.AgentRank.ToString(),
                        AvailableTools: context.Persona.AvailableTools,
                        CancellationToken: ct)).ConfigureAwait(false);

                    if (!exec.ToolFound)
                    {
                        history.AppendLine(exec.Observation);
                        context.Logger.LogWarning(
                            "Tool '{Tool}' not found. Available: {Available}",
                            action,
                            string.Join(", ", context.Persona.AvailableTools));
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(exec.ToolCall))
                    {
                        toolCalls.Add(exec.ToolCall);
                    }

                    history.AppendLine(exec.Observation);
                    context.Logger.LogInformation("👁️ {Observation}", exec.Observation);

                    if (!exec.Success && exec.Error?.Contains("required", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        history.AppendLine("Hint: Check the tool's required parameters and try again.");
                    }
                }
                catch (Exception ex)
                {
                    history.AppendLine($"Observation: Tool execution threw exception - {ex.Message}");
                    context.Logger.LogError(ex, "Tool {Tool} execution threw exception", action);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error in ReAct loop iteration {Iteration}", iterations);
                history.AppendLine($"Observation: System error occurred - {ex.Message}. Attempting to continue...");
            }

            context.SetStatus(AgentStatus.Thinking);
        }

        context.Logger.LogWarning(
            "{AgentName} reached max iterations ({Max}) without completing task",
            context.AgentName,
            MaxIterations);

        return new ReActLoopResult(
            FinalAnswer: $"Task incomplete after {MaxIterations} iterations. Partial progress:\n{history}",
            Reasoning: "Reached maximum iteration limit",
            Iterations: iterations,
            ToolCalls: toolCalls);
    }
}
