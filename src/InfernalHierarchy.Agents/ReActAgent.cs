using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core;
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
    private readonly ILlmClient _ollamaClient;
    private readonly IAgentFactory _agentFactory;
    private readonly IAgentEventSink? _eventSink;
    private readonly IVectorMemory? _vectorMemory;
    private readonly RagOptions _ragOptions;
    private readonly ReActOptions _reActOptions;
    private const int MaxIterations = 5;

    public ReActAgent(
        Agent agent,
        Persona persona,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        IAgentFactory agentFactory,
        ILlmClient ollamaClient,
        ILogger<ReActAgent> logger)
        : this(
            agent,
            persona,
            messageBus,
            sharedMemory,
            toolRegistry,
            agentFactory,
            ollamaClient,
            logger,
            eventSink: null)
    {
    }

    public ReActAgent(
        Agent agent,
        Persona persona,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        IAgentFactory agentFactory,
        ILlmClient ollamaClient,
        ILogger<ReActAgent> logger,
        IAgentEventSink? eventSink,
        IVectorMemory? vectorMemory = null,
        RagOptions? ragOptions = null,
        ReActOptions? reActOptions = null)
        : base(agent, persona, messageBus, sharedMemory, toolRegistry, logger)
    {
        _agentFactory = agentFactory;
        _ollamaClient = ollamaClient;
        _eventSink = eventSink;
        _vectorMemory = vectorMemory;
        _ragOptions = ragOptions ?? new RagOptions();
        _reActOptions = reActOptions ?? new ReActOptions();
    }

    protected override async Task<string> BuildContextAsync(AgentMessage task, CancellationToken ct)
    {
        var context = await base.BuildContextAsync(task, ct).ConfigureAwait(false);

        if (!_ragOptions.Enabled)
        {
            return context;
        }

        if (_vectorMemory == null)
        {
            return context;
        }

        IReadOnlyList<Fact> facts;
        try
        {
            facts = await _vectorMemory.SearchSimilarVisibleFactsAsync(
                task.Content,
                requestingAgentId: Id,
                requestingAgentRank: Rank,
                limit: _ragOptions.MaxFacts,
                minScore: _ragOptions.MinScore,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG retrieval failed; continuing without retrieved facts");
            return context;
        }

        if (facts.Count == 0)
        {
            return context;
        }

        var sb = new StringBuilder(context);
        sb.AppendLine("\n\n## Retrieved Facts (RAG)");

        foreach (var fact in facts)
        {
            var content = fact.Content ?? string.Empty;
            if (_ragOptions.MaxCharsPerFact > 0 && content.Length > _ragOptions.MaxCharsPerFact)
            {
                content = content[.._ragOptions.MaxCharsPerFact] + "…";
            }

            sb.AppendLine($"- [{fact.Category}] {content} (Source: {fact.Source}, Confidence: {fact.Confidence:P0})");
        }

        return sb.ToString();
    }

    public override async Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
    {
        TryAppendTaskEvent(task, InfernalHierarchy.Core.EventType.TaskReceived, "Task received");

        // Handle collaboration requests
        if (task.Content.StartsWith("[COLLABORATION_REQUEST:", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleCollaborationRequestAsync(task, ct);
        }

        // Handle special commands from Telegram
        if (task.Payload?.ContainsKey("command") == true)
        {
            var command = task.Payload["command"]?.ToString();
            if (command == "usage" || command == "models")
            {
                return await HandleCommandAsync(command, task, ct);
            }
        }

        Status = AgentStatus.Thinking;

        _logger.LogInformation("🔥 {AgentName} processing task: {Content}", Name, task.Content);

        try
        {
            TryAppendTaskEvent(task, InfernalHierarchy.Core.EventType.TaskStarted, "Task started");

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

            TryAppendDecisionEvent(task, result);

            TryAppendTaskEvent(
                task,
                InfernalHierarchy.Core.EventType.TaskCompleted,
                "Task completed",
                new Dictionary<string, object>
                {
                    ["iterations"] = result.Iterations,
                    ["tool_calls"] = result.ToolCalls.Count
                });

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

            TryAppendTaskEvent(
                task,
                InfernalHierarchy.Core.EventType.TaskFailed,
                "Task failed",
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["exception_type"] = ex.GetType().Name
                });

            return new AgentMessage
            {
                FromAgentId = Id,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = $"❌ Error: {ex.Message}"
            };
        }
    }

    private void TryAppendTaskEvent(
        AgentMessage task,
        InfernalHierarchy.Core.EventType type,
        string description,
        Dictionary<string, object>? extraMetadata = null)
    {
        if (_eventSink == null)
        {
            return;
        }

        var metadata = new Dictionary<string, object>
        {
            ["task_id"] = task.Id,
            ["from_agent_id"] = task.FromAgentId,
            ["message_type"] = task.Type.ToString(),
            ["agent_rank"] = Rank.ToString()
        };

        if (extraMetadata != null)
        {
            foreach (var kvp in extraMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        try
        {
            _eventSink.AppendEvent(new InfernalHierarchy.Core.AgentEvent
            {
                AgentId = Id,
                Type = type,
                Description = description,
                Metadata = metadata
            });
        }
        catch
        {
            // best-effort
        }
    }

    private void TryAppendDecisionEvent(AgentMessage task, ReActResult result)
    {
        if (_eventSink == null)
        {
            return;
        }

        try
        {
            _eventSink.AppendEvent(new InfernalHierarchy.Core.AgentEvent
            {
                AgentId = Id,
                Type = InfernalHierarchy.Core.EventType.DecisionMade,
                Description = "Decision recorded",
                Metadata = new Dictionary<string, object>
                {
                    ["task_id"] = task.Id,
                    ["iterations"] = result.Iterations,
                    ["reasoning"] = result.Reasoning,
                    ["answer"] = result.FinalAnswer
                }
            });
        }
        catch
        {
            // best-effort
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
                var prompt = _reActOptions.UseJsonResponse
                    ? $$"""
                        {{systemContext}}

                        # Conversation History
                        {{history}}

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
                        {"thought":"I should search memory","action":"memory_search","actionInput":{"query":"..."} }

                        Example final answer:
                        {"thought":"I am done","action":"FINAL_ANSWER","actionInput":"<final answer text>"}

                        Available tools: {{string.Join(", ", Persona.AvailableTools)}}
                        """
                    : $"""
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

                // Parse response (prefer JSON structured output when enabled)
                string thought;
                string action;
                string actionInput;
                Dictionary<string, object>? actionInputObject = null;

                if (_reActOptions.UseJsonResponse && TryParseJsonReActResponse(response, out var parsed))
                {
                    thought = parsed.Thought;
                    action = parsed.Action;
                    actionInput = parsed.ActionInputText;
                    actionInputObject = parsed.ActionInputObject;
                }
                else
                {
                    thought = ExtractSection(response, "Thought");
                    action = ExtractSection(response, "Action");
                    actionInput = ExtractSection(response, "Action Input");
                }

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
                    Dictionary<string, object> parameters;
                    if (actionInputObject != null)
                    {
                        parameters = actionInputObject;
                    }
                    else
                    {
                        parameters = ParseActionInput(actionInput, action);
                    }

                    // Add agent context for memory and agent tools
                    if (action.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
                        action.Contains("agent", StringComparison.OrdinalIgnoreCase))
                    {
                        parameters["agent_id"] = Id;
                        parameters["agent_rank"] = Rank.ToString();
                        parameters["parent_agent_id"] = Id;
                    }

                    _logger.LogDebug("Executing tool {Tool} with parameters: {Parameters}",
                        action, JsonSerializer.Serialize(parameters));

                    // Use ExecuteToolWithTrackingAsync for automatic learning integration
                    var toolResult = await _toolRegistry.ExecuteToolWithTrackingAsync(
                        action.Trim(),
                        parameters,
                        Id,
                        Rank.ToString(),
                        ct);
                    
                    if (actionInputObject != null)
                    {
                        toolCalls.Add($"{action}({JsonSerializer.Serialize(actionInputObject)})");
                    }
                    else
                    {
                        toolCalls.Add($"{action}({actionInput})");
                    }

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

    private static bool TryParseJsonReActResponse(string response, out JsonReActResponse parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        var candidate = response.Trim();

        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            candidate = Regex.Replace(candidate, "^```[a-zA-Z0-9_-]*\\s*", string.Empty);
            candidate = Regex.Replace(candidate, "\\s*```$", string.Empty);
            candidate = candidate.Trim();
        }

        if (!candidate.StartsWith("{", StringComparison.Ordinal))
        {
            var first = candidate.IndexOf('{');
            var last = candidate.LastIndexOf('}');
            if (first >= 0 && last > first)
            {
                candidate = candidate.Substring(first, last - first + 1);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(candidate);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            var thought = root.TryGetProperty("thought", out var thoughtProp) ? thoughtProp.GetString() ?? string.Empty : string.Empty;

            string actionInputText = string.Empty;
            Dictionary<string, object>? actionInputObject = null;

            if (root.TryGetProperty("actionInput", out var inputProp))
            {
                if (inputProp.ValueKind == JsonValueKind.Object)
                {
                    actionInputObject = JsonSerializer.Deserialize<Dictionary<string, object>>(inputProp.GetRawText());
                    actionInputText = inputProp.GetRawText();
                }
                else if (inputProp.ValueKind == JsonValueKind.String)
                {
                    actionInputText = inputProp.GetString() ?? string.Empty;
                }
                else
                {
                    actionInputText = inputProp.GetRawText();
                }
            }

            parsed = new JsonReActResponse(
                Thought: thought.Trim(),
                Action: action.Trim(),
                ActionInputText: actionInputText.Trim(),
                ActionInputObject: actionInputObject);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private readonly record struct JsonReActResponse(
        string Thought,
        string Action,
        string ActionInputText,
        Dictionary<string, object>? ActionInputObject);

    /// <summary>
    /// Handles special Telegram commands (usage, models)
    /// </summary>
    private async Task<AgentMessage> HandleCommandAsync(string command, AgentMessage task, CancellationToken ct)
    {
        _logger.LogInformation("📊 {AgentName} handling command: {Command}", Name, command);

        try
        {
            var response = command switch
            {
                "usage" => await GenerateUsageReportAsync(ct),
                "models" => await GenerateModelsReportAsync(ct),
                _ => $"❌ Unknown command: {command}"
            };

            // Get chat ID from payload
            var chatId = task.Payload?.ContainsKey("telegram_chat_id") == true
                ? Convert.ToInt64(task.Payload["telegram_chat_id"])
                : 0;

            // Send response via Telegram if chat ID available
            if (chatId != 0)
            {
                var telegramTool = _toolRegistry.GetTool("telegram_send");
                if (telegramTool != null)
                {
                    await telegramTool.ExecuteAsync(new Dictionary<string, object>
                    {
                        ["chat_id"] = chatId,
                        ["message"] = response
                    }, ct);
                }
            }

            return new AgentMessage
            {
                FromAgentId = Id,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = response
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle command: {Command}", command);
            return new AgentMessage
            {
                FromAgentId = Id,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = $"❌ Error handling command: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Generate token usage statistics report
    /// </summary>
    private async Task<string> GenerateUsageReportAsync(CancellationToken ct)
    {
        // Get TokenUsageTracker from tool registry
        var tokenTracker = _toolRegistry.GetService<TokenUsageTracker>();
        
        if (tokenTracker == null)
        {
            _logger.LogWarning("TokenUsageTracker not available");
            return "⚠️ Token usage tracking not available";
        }

        var stats = tokenTracker.GetOverallStats();
        
        var report = new StringBuilder();
        report.AppendLine("📊 **Token Usage Statistics**\n");
        report.AppendLine($"**Total Calls:** {stats.TotalCalls:N0}");
        report.AppendLine($"**Input Tokens:** {stats.TotalInputTokens:N0}");
        report.AppendLine($"**Output Tokens:** {stats.TotalOutputTokens:N0}");
        report.AppendLine($"**Total Tokens:** {stats.TotalTokens:N0}");
        report.AppendLine($"**Avg Duration:** {stats.AverageDuration.TotalMilliseconds:F0}ms\n");

        if (stats.ModelBreakdown.Any())
        {
            report.AppendLine("**Per-Model Breakdown:**");
            foreach (var kvp in stats.ModelBreakdown.OrderByDescending(x => x.Value.CallCount))
            {
                var totalTokens = kvp.Value.TotalInputTokens + kvp.Value.TotalOutputTokens;
                report.AppendLine($"  • {kvp.Key}: {kvp.Value.CallCount:N0} calls, {totalTokens:N0} tokens");
            }
        }

        await Task.CompletedTask; // Keep async signature for consistency
        return report.ToString();
    }

    /// <summary>
    /// Generate available LLM models report
    /// </summary>
    private async Task<string> GenerateModelsReportAsync(CancellationToken ct)
    {
        // Get MultiModelLlmClient from tool registry
        var llmClient = _toolRegistry.GetService<MultiModelLlmClient>();
        
        if (llmClient == null)
        {
            _logger.LogWarning("MultiModelLlmClient not available");
            return "⚠️ LLM model information not available";
        }

        var models = llmClient.GetAvailableModels();
        
        var report = new StringBuilder();
        report.AppendLine("🧠 **Available LLM Models**\n");
        
        foreach (var model in models)
        {
            report.AppendLine($"**{model.Name}**");
            report.AppendLine($"  Complexity: {model.Complexity}");
            report.AppendLine($"  Max Tokens: {model.MaxTokens:N0}");
            report.AppendLine($"  Temperature: {model.Temperature}");
            report.AppendLine();
        }

        await Task.CompletedTask; // Keep async signature for consistency
        return report.ToString();
    }

    /// <summary>
    /// Handle collaboration request from other agents
    /// </summary>
    private async Task<AgentMessage> HandleCollaborationRequestAsync(AgentMessage message, CancellationToken ct)
    {
        try
        {
            // Parse collaboration request ID and task
            var match = Regex.Match(message.Content, @"\[COLLABORATION_REQUEST:([^\]]+)\]\s*(.+)", RegexOptions.Singleline);
            if (!match.Success)
            {
                _logger.LogWarning("Invalid collaboration request format: {Content}", message.Content);
                return CreateErrorResponse(message.FromAgentId, "Invalid collaboration request format");
            }

            var requestId = match.Groups[1].Value;
            var task = match.Groups[2].Value;

            _logger.LogInformation(
                "🤝 {AgentName} processing collaboration request {RequestId}: {Task}",
                Name,
                requestId,
                task.Length > 100 ? task[..100] + "..." : task);

            Status = AgentStatus.Thinking;

            // Process the task to generate response
            var context = await BuildContextAsync(message, ct);
            var result = await RunReActLoopAsync(context, task, ct);

            Status = AgentStatus.Idle;

            // Calculate confidence based on reasoning quality and tool success
            var confidence = CalculateConfidence(result);

            // Submit response to collaboration service
            var collaborationService = _toolRegistry.GetService<IAgentCollaborationService>();
            if (collaborationService != null)
            {
                var response = new AgentResponse
                {
                    AgentId = Id,
                    AgentRank = Rank,
                    Response = result.FinalAnswer,
                    Confidence = confidence,
                    Reasoning = result.Reasoning,
                    Timestamp = DateTime.UtcNow,
                    ProcessingTimeMs = result.Iterations * 1000 // Rough estimate
                };

                await collaborationService.SubmitResponseAsync(requestId, response, ct);

                _logger.LogInformation(
                    "✅ {AgentName} submitted collaboration response with confidence {Confidence:F2}",
                    Name,
                    confidence);
            }
            else
            {
                _logger.LogWarning("IAgentCollaborationService not available");
            }

            // Return acknowledgment (collaboration service handles actual result aggregation)
            return new AgentMessage
            {
                FromAgentId = Id,
                ToAgentId = message.FromAgentId,
                Type = MessageType.Report,
                Content = $"Collaboration response submitted: {result.FinalAnswer[..Math.Min(100, result.FinalAnswer.Length)]}..."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle collaboration request");
            Status = AgentStatus.Idle;
            return CreateErrorResponse(message.FromAgentId, $"Error: {ex.Message}");
        }
    }

    private double CalculateConfidence(ReActResult result)
    {
        // Base confidence on successful completion
        var confidence = 0.5;

        // Increase confidence if reached conclusion within iterations
        if (result.Iterations < MaxIterations)
        {
            confidence += 0.2;
        }

        // Increase confidence if tools were used successfully
        if (result.ToolCalls.Count > 0)
        {
            confidence += 0.2;
        }

        // Increase confidence if reasoning is detailed
        if (result.Reasoning.Length > 200)
        {
            confidence += 0.1;
        }

        return Math.Min(1.0, confidence);
    }

    private AgentMessage CreateErrorResponse(string toAgentId, string errorMessage)
    {
        return new AgentMessage
        {
            FromAgentId = Id,
            ToAgentId = toAgentId,
            Type = MessageType.Report,
            Content = $"❌ {errorMessage}"
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
