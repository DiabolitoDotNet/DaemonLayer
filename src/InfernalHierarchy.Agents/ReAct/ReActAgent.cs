using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using InfernalHierarchy.Tools.Clients;
using InfernalHierarchy.Tools.Telemetry;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfernalHierarchy.Agents.ReAct;

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
    private readonly TokenUsageTracker? _tokenUsageTracker;
    private readonly MultiModelLlmClient? _multiModelLlmClient;
    private readonly IAgentCollaborationService? _collaborationService;
    private readonly IActionParser _actionParser;
    private readonly IActionExecutor _actionExecutor;
    private readonly IReportGenerator _reportGenerator;
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
        ReActOptions? reActOptions = null,
        TokenUsageTracker? tokenUsageTracker = null,
        MultiModelLlmClient? multiModelLlmClient = null,
        IAgentCollaborationService? collaborationService = null,
        IActionParser? actionParser = null,
        IActionExecutor? actionExecutor = null,
        IReportGenerator? reportGenerator = null)
        : base(agent, persona, messageBus, sharedMemory, toolRegistry, logger)
    {
        _agentFactory = agentFactory;
        _ollamaClient = ollamaClient;
        _eventSink = eventSink;
        _vectorMemory = vectorMemory;
        _ragOptions = ragOptions ?? new RagOptions();
        _reActOptions = reActOptions ?? new ReActOptions();
        _tokenUsageTracker = tokenUsageTracker;
        _multiModelLlmClient = multiModelLlmClient;
        _collaborationService = collaborationService;

        _actionParser = actionParser ?? new DefaultActionParser();
        var inputParser = new DefaultActionInputParser(_logger);
        _actionExecutor = actionExecutor ?? new DefaultActionExecutor(inputParser);
        _reportGenerator = reportGenerator ?? new DefaultReportGenerator(_tokenUsageTracker, _multiModelLlmClient);
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
        TryAppendTaskEvent(task, EventType.TaskReceived, "Task received");

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
            TryAppendTaskEvent(task, EventType.TaskStarted, "Task started");

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
                EventType.TaskCompleted,
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
                EventType.TaskFailed,
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
        EventType type,
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
            _eventSink.AppendEvent(new AgentEvent
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
            _eventSink.AppendEvent(new AgentEvent
            {
                AgentId = Id,
                Type = EventType.DecisionMade,
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
                        {\"thought\":\"I should search memory\",\"action\":\"memory_search\",\"actionInput\":{\"query\":\"...\"} }

                        Example final answer:
                        {\"thought\":\"I am done\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"<final answer text>\"}

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

                if (!_actionParser.TryParse(response, _reActOptions.UseJsonResponse, out var parsed))
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

                var thought = parsed.Thought;
                var action = parsed.Action;
                var actionInput = parsed.ActionInputText;
                var actionInputObject = parsed.ActionInputObject;

                _logger.LogInformation("💭 Thought: {Thought}", thought);
                _logger.LogInformation("⚡ Action: {Action}", action);

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

                try
                {
                    Status = AgentStatus.ActingWithTool;

                    var exec = await _actionExecutor.ExecuteAsync(new ActionExecutionContext(
                        ToolRegistry: _toolRegistry,
                        ToolName: action,
                        ActionInputText: actionInput,
                        ActionInputObject: actionInputObject,
                        AgentId: Id,
                        AgentRank: Rank.ToString(),
                        AvailableTools: Persona.AvailableTools,
                        CancellationToken: ct)).ConfigureAwait(false);

                    if (!exec.ToolFound)
                    {
                        history.AppendLine(exec.Observation);
                        _logger.LogWarning("Tool '{Tool}' not found. Available: {Available}", action, string.Join(", ", Persona.AvailableTools));
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(exec.ToolCall))
                    {
                        toolCalls.Add(exec.ToolCall);
                    }

                    history.AppendLine(exec.Observation);
                    _logger.LogInformation("👁️ {Observation}", exec.Observation);

                    if (!exec.Success && exec.Error?.Contains("required", StringComparison.OrdinalIgnoreCase) == true)
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
                "usage" => await _reportGenerator.GenerateUsageReportAsync(ct),
                "models" => await _reportGenerator.GenerateModelsReportAsync(ct),
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
            if (_collaborationService != null)
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

                await _collaborationService.SubmitResponseAsync(requestId, response, ct);

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
