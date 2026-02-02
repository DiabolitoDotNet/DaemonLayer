using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfernalHierarchy.Agents;

/// <summary>
/// ReAct (Reasoning + Acting) agent implementation
/// Implements the Thought → Action → Observation loop
/// </summary>
public class ReActAgent : BaseAgent
{
    private readonly OllamaClient _ollamaClient;
    private readonly IAgentFactory _agentFactory;
    private const int MaxIterations = 5;

    public ReActAgent(
        Agent agent,
        Persona persona,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        IAgentFactory agentFactory,
        OllamaClient ollamaClient,
        ILogger<ReActAgent> logger)
        : base(agent, persona, messageBus, sharedMemory, toolRegistry, logger)
    {
        _agentFactory = agentFactory;
        _ollamaClient = ollamaClient;
    }

    public override async Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
    {
        Status = AgentStatus.Thinking;

        _logger.LogInformation("🔥 {AgentName} processing task: {Content}", Name, task.Content);

        try
        {
            var context = await BuildContextAsync(task, ct);
            var result = await RunReActLoopAsync(context, task.Content, ct);

            // Record decision
            await _sharedMemory.AddDecisionAsync(new Decision
            {
                CreatedBy = Id,
                Context = task.Content,
                Action = result.FinalAnswer,
                Reasoning = result.Reasoning
            }, ct);

            Status = AgentStatus.Idle;

            return new AgentMessage
            {
                FromAgentId = Id,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = result.FinalAnswer,
                Payload = new Dictionary<string, object>
                {
                    ["reasoning"] = result.Reasoning,
                    ["iterations"] = result.Iterations,
                    ["tool_calls"] = result.ToolCalls
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process task");
            Status = AgentStatus.Idle;

            return new AgentMessage
            {
                FromAgentId = Id,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = $"❌ Error: {ex.Message}"
            };
        }
    }

    private async Task<ReActResult> RunReActLoopAsync(string systemContext, string task, CancellationToken ct)
    {
        var history = new StringBuilder();
        var toolCalls = new List<string>();
        var iterations = 0;
        var consecutiveParseFailures = 0;
        const int maxParseFailures = 3;

        history.AppendLine($"Task: {task}\n");

        while (iterations < MaxIterations)
        {
            iterations++;
            Status = AgentStatus.Thinking;

            try
            {
                // Build prompt with history
                var prompt = $"""
                    {systemContext}

                    # Conversation History
                    {history}

                    # Instructions
                    Follow the ReAct pattern:
                    1. Thought: Analyze what you need to do next
                    2. Action: Choose a tool to use (or FINAL_ANSWER if done)
                    3. Provide your response in this exact format:

                    Thought: <your reasoning>
                    Action: <tool_name or FINAL_ANSWER>
                    Action Input: <tool parameters as JSON or final answer text>

                    Available tools: {string.Join(", ", Persona.AvailableTools)}
                    """;

                _logger.LogDebug("Iteration {Iteration}: Calling LLM", iterations);

                var response = await _ollamaClient.GetCompletionAsync(
                    Persona.SystemPrompt,
                    prompt,
                    ct);

                if (string.IsNullOrWhiteSpace(response))
                {
                    _logger.LogWarning("LLM returned empty response");
                    history.AppendLine("Observation: LLM returned empty response. Retrying...");
                    continue;
                }

                history.AppendLine($"\n--- Iteration {iterations} ---");
                history.AppendLine(response);

                // Parse response with improved extraction
                var thought = ExtractSection(response, "Thought");
                var action = ExtractSection(response, "Action");
                var actionInput = ExtractSection(response, "Action Input");

                _logger.LogInformation("💭 Thought: {Thought}", thought);
                _logger.LogInformation("⚡ Action: {Action}", action);

                if (string.IsNullOrEmpty(action))
                {
                    consecutiveParseFailures++;
                    _logger.LogWarning("Failed to parse action from response (failure {Count}/{Max})",
                        consecutiveParseFailures, maxParseFailures);

                    if (consecutiveParseFailures >= maxParseFailures)
                    {
                        return new ReActResult
                        {
                            FinalAnswer = "Unable to complete task due to repeated parsing failures.",
                            Reasoning = "LLM responses did not follow expected format",
                            Iterations = iterations,
                            ToolCalls = toolCalls
                        };
                    }

                    history.AppendLine("Observation: Response format incorrect. Please follow the Thought/Action/Action Input format exactly.");
                    continue;
                }

                // Reset parse failure counter on successful parse
                consecutiveParseFailures = 0;

                // Check if done
                if (action.Contains("FINAL_ANSWER", StringComparison.OrdinalIgnoreCase))
                {
                    return new ReActResult
                    {
                        FinalAnswer = actionInput,
                        Reasoning = thought,
                        Iterations = iterations,
                        ToolCalls = toolCalls
                    };
                }

                // Execute tool
                Status = AgentStatus.ActingWithTool;
                var tool = _toolRegistry.GetTool(action.Trim());

                if (tool == null)
                {
                    var availableTools = string.Join(", ", Persona.AvailableTools);
                    history.AppendLine($"Observation: Tool '{action}' not found. Available tools: {availableTools}");
                    _logger.LogWarning("Tool '{Tool}' not found. Available: {Available}", action, availableTools);
                    continue;
                }

                try
                {
                    var parameters = ParseActionInput(actionInput, action);

                    // Add agent_id for memory and agent tools
                    if (action.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
                        action.Contains("agent", StringComparison.OrdinalIgnoreCase))
                    {
                        parameters["agent_id"] = Id;
                        parameters["parent_agent_id"] = Id;
                    }

                    _logger.LogDebug("Executing tool {Tool} with parameters: {Parameters}",
                        action, JsonSerializer.Serialize(parameters));

                    var toolResult = await tool.ExecuteAsync(parameters, ct);
                    toolCalls.Add($"{action}({actionInput})");

                    var observation = toolResult.Success
                        ? $"Observation: {toolResult.Output}"
                        : $"Observation: Tool execution failed - {toolResult.Error}";

                    history.AppendLine(observation);
                    _logger.LogInformation("👁️ {Observation}", observation);

                    // If tool failed critically, provide guidance
                    if (!toolResult.Success && toolResult.Error?.Contains("required", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        history.AppendLine("Hint: Check the tool's required parameters and try again.");
                    }
                }
                catch (Exception ex)
                {
                    history.AppendLine($"Observation: Tool execution threw exception - {ex.Message}");
                    _logger.LogError(ex, "Tool {Tool} execution threw exception", action);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // Propagate cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReAct loop iteration {Iteration}", iterations);
                history.AppendLine($"Observation: System error occurred - {ex.Message}. Attempting to continue...");
            }

            Status = AgentStatus.Thinking;
        }

        // Max iterations reached
        _logger.LogWarning("{AgentName} reached max iterations ({Max}) without completing task", Name, MaxIterations);
        return new ReActResult
        {
            FinalAnswer = $"Task incomplete after {MaxIterations} iterations. Partial progress:\n{history}",
            Reasoning = "Reached maximum iteration limit",
            Iterations = iterations,
            ToolCalls = toolCalls
        };
    }

    private string ExtractSection(string text, string sectionName)
    {
        // Try multiple patterns for robustness
        var patterns = new[]
        {
            $@"{sectionName}:\s*(.+?)(?=\n(?:Thought|Action|Observation|---|\Z))",
            $@"{sectionName}\s*:\s*(.+?)(?=\n|$)",
            $@"(?i){sectionName}:\s*(.+?)(?=\n(?:thought|action|observation|---|\Z))"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return string.Empty;
    }

    private Dictionary<string, object> ParseActionInput(string input, string actionName)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new Dictionary<string, object>();
        }

        try
        {
            // Try parsing as JSON first
            var trimmedInput = input.Trim();
            if (trimmedInput.StartsWith("{") && trimmedInput.EndsWith("}"))
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(trimmedInput)
                       ?? new Dictionary<string, object>();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("JSON parsing failed: {Error}. Treating as plain text.", ex.Message);
        }

        // Fallback: treat as plain text query with multiple key variations
        return new Dictionary<string, object>
        {
            ["query"] = input,
            ["content"] = input,
            ["text"] = input,
            ["message"] = input
        };
    }

    private class ReActResult
    {
        public string FinalAnswer { get; set; } = string.Empty;
        public string Reasoning { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public List<string> ToolCalls { get; set; } = new();
    }
}
