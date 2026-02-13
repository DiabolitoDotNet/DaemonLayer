using System.Text.RegularExpressions;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultReActTaskProcessor : IReActTaskProcessor
{
    private const int MaxIterations = 5;

    private readonly IRagContextEnricher _ragContextEnricher;
    private readonly IAgentEventAppender _eventAppender;

    public DefaultReActTaskProcessor(IRagContextEnricher ragContextEnricher, IAgentEventAppender eventAppender)
    {
        _ragContextEnricher = ragContextEnricher;
        _eventAppender = eventAppender;
    }

    public async Task<AgentMessage> ProcessAsync(ReActTaskProcessorContext context, AgentMessage task, CancellationToken ct)
    {
        var effectiveContext = context with { Persona = BuildEffectivePersonaForTask(context.Persona, task) };

        _eventAppender.TryAppendTaskEvent(context.EventSink, context.AgentId, context.AgentRank, task, EventType.TaskReceived, "Task received");

        if (IsCollaborationRequest(task))
        {
            return await HandleCollaborationRequestAsync(effectiveContext, task, ct).ConfigureAwait(false);
        }

        if (TryGetTelegramCommand(task, out var command) && (command == "usage" || command == "models"))
        {
            return await HandleTelegramCommandAsync(effectiveContext, command, task, ct).ConfigureAwait(false);
        }

        var effectiveTaskContent = task.Content;
        if (IsSupervisorReplan(task))
        {
            effectiveTaskContent = BuildSupervisorReplanTaskContent(task);
        }

        if (IsSupervisorReplan(task))
        {
            effectiveContext.SetStatus(AgentStatus.Thinking);
            effectiveContext.Logger.LogInformation("🔥 {AgentName} processing supervisor replan request", effectiveContext.AgentName);

            try
            {
                _eventAppender.TryAppendTaskEvent(
                    effectiveContext.EventSink,
                    effectiveContext.AgentId,
                    effectiveContext.AgentRank,
                    task,
                    EventType.TaskStarted,
                    "Supervisor replan started");

                var response = effectiveContext.LlmClient is ITunableLlmClient tunable
                    ? await tunable.GetCompletionWithOptionsAsync(
                        effectiveContext.Persona.SystemPrompt,
                        effectiveTaskContent,
                        temperature: 0.2,
                        maxTokens: 512,
                        ct).ConfigureAwait(false)
                    : await effectiveContext.LlmClient.GetCompletionAsync(
                        effectiveContext.Persona.SystemPrompt,
                        effectiveTaskContent,
                        ct).ConfigureAwait(false);

                _eventAppender.TryAppendTaskEvent(
                    effectiveContext.EventSink,
                    effectiveContext.AgentId,
                    effectiveContext.AgentRank,
                    task,
                    EventType.TaskCompleted,
                    "Supervisor replan completed",
                    new Dictionary<string, object>
                    {
                        ["mode"] = "one_shot",
                        ["max_tokens"] = 512
                    });

                effectiveContext.SetStatus(AgentStatus.Idle);

                return new AgentMessage
                {
                    FromAgentId = effectiveContext.AgentId,
                    ToAgentId = task.FromAgentId,
                    Type = MessageType.Report,
                    Content = response
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                effectiveContext.Logger.LogError(ex, "Failed to process supervisor replan task");
                effectiveContext.SetStatus(AgentStatus.Idle);

                _eventAppender.TryAppendTaskEvent(
                    effectiveContext.EventSink,
                    effectiveContext.AgentId,
                    effectiveContext.AgentRank,
                    task,
                    EventType.TaskFailed,
                    "Supervisor replan failed",
                    new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["exception_type"] = ex.GetType().Name
                    });

                return new AgentMessage
                {
                    FromAgentId = effectiveContext.AgentId,
                    ToAgentId = task.FromAgentId,
                    Type = MessageType.Report,
                    Content = $"❌ Error: {ex.Message}",
                    Payload = new Dictionary<string, object>(task.Payload ?? new Dictionary<string, object>())
                };
            }
        }

        effectiveContext.SetStatus(AgentStatus.Thinking);
        effectiveContext.Logger.LogInformation("🔥 {AgentName} processing task: {Content}", effectiveContext.AgentName, effectiveTaskContent);

        try
        {
            _eventAppender.TryAppendTaskEvent(effectiveContext.EventSink, effectiveContext.AgentId, effectiveContext.AgentRank, task, EventType.TaskStarted, "Task started");

            var baseContext = await effectiveContext.BuildBaseContextAsync(task, ct).ConfigureAwait(false);
            var systemContext = await _ragContextEnricher.EnrichAsync(
                baseContext,
                query: effectiveTaskContent,
                agentId: effectiveContext.AgentId,
                agentRank: effectiveContext.AgentRank,
                vectorMemory: effectiveContext.VectorMemory,
                ragOptions: effectiveContext.RagOptions,
                logger: effectiveContext.Logger,
                ct: ct).ConfigureAwait(false);

            systemContext = AppendRuntimeConstraints(systemContext, effectiveContext.Persona, task);

            var result = await RunLoopAsync(effectiveContext, systemContext, effectiveTaskContent, ct).ConfigureAwait(false);

            await effectiveContext.SharedMemory.AddDecisionAsync(new Decision
            {
                CreatedBy = effectiveContext.AgentId,
                Context = task.Content,
                Action = result.FinalAnswer,
                Reasoning = result.Reasoning
            }, ct).ConfigureAwait(false);

            _eventAppender.TryAppendDecisionEvent(effectiveContext.EventSink, effectiveContext.AgentId, task, result.Iterations, result.Reasoning, result.FinalAnswer);

            _eventAppender.TryAppendTaskEvent(
                effectiveContext.EventSink,
                effectiveContext.AgentId,
                effectiveContext.AgentRank,
                task,
                EventType.TaskCompleted,
                "Task completed",
                new Dictionary<string, object>
                {
                    ["iterations"] = result.Iterations,
                    ["tool_calls"] = result.ToolCalls.Count
                });

            effectiveContext.SetStatus(AgentStatus.Idle);

            var basePayload = task.Payload ?? new Dictionary<string, object>();
            var responsePayload = new Dictionary<string, object>(basePayload)
            {
                ["reasoning"] = result.Reasoning,
                ["iterations"] = result.Iterations,
                ["tool_calls"] = result.ToolCalls
            };

            return new AgentMessage
            {
                FromAgentId = effectiveContext.AgentId,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = result.FinalAnswer,
                Payload = responsePayload
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            effectiveContext.Logger.LogError(ex, "Failed to process task");
            effectiveContext.SetStatus(AgentStatus.Idle);

            _eventAppender.TryAppendTaskEvent(
                effectiveContext.EventSink,
                effectiveContext.AgentId,
                effectiveContext.AgentRank,
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
                FromAgentId = effectiveContext.AgentId,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = $"❌ Error: {ex.Message}",
                Payload = new Dictionary<string, object>(task.Payload ?? new Dictionary<string, object>())
            };
        }
    }

    private static Persona BuildEffectivePersonaForTask(Persona persona, AgentMessage task)
    {
        var isHttp = task.Payload is not null
            && task.Payload.TryGetValue("transport", out var transportObj)
            && transportObj is not null
            && string.Equals(transportObj.ToString(), "http", StringComparison.OrdinalIgnoreCase);

        var tools = persona.AvailableTools.AsEnumerable();

        if (persona.AvailableTools.Contains("send_telegram", StringComparer.OrdinalIgnoreCase))
        {
            // Only allow send_telegram when a concrete telegram_chat_id exists.
            if (!TryGetTelegramChatId(task.Payload, out var chatId) || chatId == 0)
            {
                tools = tools.Where(t => !string.Equals(t, "send_telegram", StringComparison.OrdinalIgnoreCase));
            }
        }

        if (isHttp)
        {
            // HTTP transport should not trigger internal agent-to-agent messaging.
            tools = tools.Where(t => !string.Equals(t, "send_agent_message", StringComparison.OrdinalIgnoreCase));
        }

        var filtered = tools.ToArray();

        if (filtered.SequenceEqual(persona.AvailableTools, StringComparer.OrdinalIgnoreCase))
        {
            return persona;
        }

        return new Persona
        {
            Name = persona.Name,
            DemonTitle = persona.DemonTitle,
            SystemPrompt = persona.SystemPrompt,
            ModelOverride = persona.ModelOverride,
            Personality = persona.Personality,
            Specializations = persona.Specializations,
            AvailableTools = filtered,
            CustomInstructions = new Dictionary<string, string>(persona.CustomInstructions)
        };
    }

    private static bool TryGetTelegramChatId(Dictionary<string, object>? payload, out long chatId)
    {
        chatId = 0;

        if (payload is null || !payload.TryGetValue("telegram_chat_id", out var raw) || raw is null)
        {
            return false;
        }

        try
        {
            chatId = raw switch
            {
                long l => l,
                int i => i,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => Convert.ToInt64(raw)
            };

            return chatId != 0;
        }
        catch
        {
            chatId = 0;
            return false;
        }
    }

    private static string AppendRuntimeConstraints(string systemContext, Persona persona, AgentMessage task)
    {
        var allowed = persona.AvailableTools.Count == 0
            ? "(none)"
            : string.Join(", ", persona.AvailableTools);

        var hasTelegram = TryGetTelegramChatId(task.Payload, out var chatId) && chatId != 0;
        var transport = task.Payload?.TryGetValue("transport", out var t) == true ? t?.ToString() : null;

        var agentCountEmailRule = BuildAgentCountEmailRule(task, persona);

        return $"""
            {systemContext}

            # Runtime Constraints (STRICT)
            - Allowed tools for this task: {allowed}
            - Action MUST be FINAL_ANSWER or one of the allowed tools above.
            - Do NOT call send_telegram unless a real telegram_chat_id is present in the task payload.
            {agentCountEmailRule}
            - transport={transport ?? "(unknown)"} telegram_chat_id={(hasTelegram ? chatId.ToString() : "(none)")}
            """;
    }

    private static string BuildAgentCountEmailRule(AgentMessage task, Persona persona)
    {
        var content = task.Content ?? string.Empty;

        if (!content.Contains("mail", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("email", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("e-mail", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!content.Contains("agent", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!content.Contains("decompte", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("décompte", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("count", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!persona.AvailableTools.Contains("get_agent_status", StringComparer.OrdinalIgnoreCase)
            || !persona.AvailableTools.Contains("email_send", StringComparer.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return "- For agent count emails: you MUST call get_agent_status first, then include numeric total_agents / occupied_agents / idle_agents in the email body before calling email_send (no templates like ${total_agents}).";
    }

    private static bool IsSupervisorReplan(AgentMessage task)
    {
        if (task.Type != MessageType.Command)
        {
            return false;
        }

        if (task.Content.StartsWith("SUPERVISOR_REPLAN:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (task.Payload?.TryGetValue("supervisor_action", out var action) == true)
        {
            return string.Equals(action?.ToString(), "replan", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string BuildSupervisorReplanTaskContent(AgentMessage task)
    {
        var reason = task.Content;
        if (reason.StartsWith("SUPERVISOR_REPLAN:", StringComparison.OrdinalIgnoreCase))
        {
            reason = reason["SUPERVISOR_REPLAN:".Length..].Trim();
        }

        return $"""
            SUPERVISOR REPLAN REQUEST
            Reason: {reason}

            You are the root agent. Recover from a stall/loop and produce an updated, concrete plan.

            Output format:
            1) Diagnosis (why progress stalled)
            2) Updated plan (5-12 numbered steps, each testable)
            3) Immediate next step (do it now)
            4) If you suspect runaway sub-agents: list which agents/ranks should be preempted and why (do not spawn new agents unless necessary).
            """;
    }

    private static bool IsCollaborationRequest(AgentMessage task) =>
        task.Content.StartsWith("[COLLABORATION_REQUEST:", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetTelegramCommand(AgentMessage task, out string command)
    {
        command = string.Empty;

        if (task.Payload?.ContainsKey("command") != true)
        {
            return false;
        }

        command = task.Payload["command"]?.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(command);
    }

    private async Task<ReActResult> RunLoopAsync(ReActTaskProcessorContext context, string systemContext, string task, CancellationToken ct)
    {
        var loopContext = new ReActLoopContext(
            SystemContext: systemContext,
            Task: task,
            Persona: context.Persona,
            LlmClient: context.LlmClient,
            ToolRegistry: context.ToolRegistry,
            ActionParser: context.ActionParser,
            ActionExecutor: context.ActionExecutor,
            Logger: context.Logger,
            SetStatus: context.SetStatus,
            AgentId: context.AgentId,
            AgentName: context.AgentName,
            AgentRank: context.AgentRank,
            ReActOptions: context.ReActOptions,
            PromptBuilder: context.PromptBuilder);

        var result = await context.LoopRunner.RunAsync(loopContext, ct).ConfigureAwait(false);

        return new ReActResult(
            FinalAnswer: result.FinalAnswer,
            Reasoning: result.Reasoning,
            Iterations: result.Iterations,
            ToolCalls: result.ToolCalls.ToList());
    }

    private async Task<AgentMessage> HandleTelegramCommandAsync(ReActTaskProcessorContext context, string command, AgentMessage task, CancellationToken ct)
    {
        context.Logger.LogInformation("📊 {AgentName} handling command: {Command}", context.AgentName, command);

        try
        {
            var response = command switch
            {
                "usage" => await context.ReportGenerator.GenerateUsageReportAsync(ct).ConfigureAwait(false),
                "models" => await context.ReportGenerator.GenerateModelsReportAsync(ct).ConfigureAwait(false),
                _ => $"❌ Unknown command: {command}"
            };

            var chatId = task.Payload?.ContainsKey("telegram_chat_id") == true
                ? Convert.ToInt64(task.Payload["telegram_chat_id"])
                : 0;

            if (chatId != 0)
            {
                var telegramTool = context.ToolRegistry.GetTool("send_telegram");
                if (telegramTool != null)
                {
                    await telegramTool.ExecuteAsync(new Dictionary<string, object>
                    {
                        ["chat_id"] = chatId,
                        ["message"] = response
                    }, ct).ConfigureAwait(false);
                }
            }

            return new AgentMessage
            {
                FromAgentId = context.AgentId,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = response
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Failed to handle command: {Command}", command);
            return new AgentMessage
            {
                FromAgentId = context.AgentId,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = $"❌ Error handling command: {ex.Message}"
            };
        }
    }

    private async Task<AgentMessage> HandleCollaborationRequestAsync(ReActTaskProcessorContext context, AgentMessage message, CancellationToken ct)
    {
        try
        {
            var match = Regex.Match(
                message.Content,
                @"\[COLLABORATION_REQUEST:([^\]]+)\]\s*(.+)",
                RegexOptions.Singleline);

            if (!match.Success)
            {
                context.Logger.LogWarning("Invalid collaboration request format: {Content}", message.Content);
                return CreateErrorResponse(context.AgentId, message.FromAgentId, "Invalid collaboration request format");
            }

            var requestId = match.Groups[1].Value;
            var task = match.Groups[2].Value;

            var round = 1;
            if (message.Payload != null && message.Payload.TryGetValue("Round", out var roundObj) && roundObj != null)
            {
                _ = int.TryParse(roundObj.ToString(), out round);
                if (round <= 0)
                {
                    round = 1;
                }
            }

            context.Logger.LogInformation(
                "🤝 {AgentName} processing collaboration request {RequestId}: {Task}",
                context.AgentName,
                requestId,
                task.Length > 100 ? task[..100] + "..." : task);

            context.SetStatus(AgentStatus.Thinking);

            var baseContext = await context.BuildBaseContextAsync(message, ct).ConfigureAwait(false);
            var systemContext = await _ragContextEnricher.EnrichAsync(
                baseContext,
                query: message.Content,
                agentId: context.AgentId,
                agentRank: context.AgentRank,
                vectorMemory: context.VectorMemory,
                ragOptions: context.RagOptions,
                logger: context.Logger,
                ct: ct).ConfigureAwait(false);

            var result = await RunLoopAsync(context, systemContext, task, ct).ConfigureAwait(false);

            context.SetStatus(AgentStatus.Idle);

            var confidence = CalculateConfidence(result);

            if (context.CollaborationService != null)
            {
                var response = new AgentResponse
                {
                    AgentId = context.AgentId,
                    AgentRank = context.AgentRank,
                    Response = result.FinalAnswer,
                    Confidence = confidence,
                    Reasoning = result.Reasoning,
                    Timestamp = DateTime.UtcNow,
                    ProcessingTimeMs = result.Iterations * 1000,
                    Round = round
                };

                await context.CollaborationService.SubmitResponseAsync(requestId, response, ct).ConfigureAwait(false);

                context.Logger.LogInformation(
                    "✅ {AgentName} submitted collaboration response with confidence {Confidence:F2}",
                    context.AgentName,
                    confidence);
            }
            else
            {
                context.Logger.LogWarning("IAgentCollaborationService not available");
            }

            var preview = result.FinalAnswer[..Math.Min(100, result.FinalAnswer.Length)];
            return new AgentMessage
            {
                FromAgentId = context.AgentId,
                ToAgentId = message.FromAgentId,
                Type = MessageType.Report,
                Content = $"Collaboration response submitted: {preview}..."
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Failed to handle collaboration request");
            context.SetStatus(AgentStatus.Idle);
            return CreateErrorResponse(context.AgentId, message.FromAgentId, $"Error: {ex.Message}");
        }
    }

    private static double CalculateConfidence(ReActResult result)
    {
        var confidence = 0.5;

        if (result.Iterations < MaxIterations)
        {
            confidence += 0.2;
        }

        if (result.ToolCalls.Count > 0)
        {
            confidence += 0.2;
        }

        if (result.Reasoning.Length > 200)
        {
            confidence += 0.1;
        }

        return Math.Min(1.0, confidence);
    }

    private static AgentMessage CreateErrorResponse(string fromAgentId, string toAgentId, string errorMessage) =>
        new()
        {
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            Type = MessageType.Report,
            Content = $"❌ {errorMessage}"
        };

    private sealed record ReActResult(string FinalAnswer, string Reasoning, int Iterations, List<string> ToolCalls);
}
