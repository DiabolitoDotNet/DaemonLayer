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
        _eventAppender.TryAppendTaskEvent(context.EventSink, context.AgentId, context.AgentRank, task, EventType.TaskReceived, "Task received");

        if (IsCollaborationRequest(task))
        {
            return await HandleCollaborationRequestAsync(context, task, ct).ConfigureAwait(false);
        }

        if (TryGetTelegramCommand(task, out var command) && (command == "usage" || command == "models"))
        {
            return await HandleTelegramCommandAsync(context, command, task, ct).ConfigureAwait(false);
        }

        context.SetStatus(AgentStatus.Thinking);
        context.Logger.LogInformation("🔥 {AgentName} processing task: {Content}", context.AgentName, task.Content);

        try
        {
            _eventAppender.TryAppendTaskEvent(context.EventSink, context.AgentId, context.AgentRank, task, EventType.TaskStarted, "Task started");

            var baseContext = await context.BuildBaseContextAsync(task, ct).ConfigureAwait(false);
            var systemContext = await _ragContextEnricher.EnrichAsync(
                baseContext,
                query: task.Content,
                agentId: context.AgentId,
                agentRank: context.AgentRank,
                vectorMemory: context.VectorMemory,
                ragOptions: context.RagOptions,
                logger: context.Logger,
                ct: ct).ConfigureAwait(false);

            var result = await RunLoopAsync(context, systemContext, task.Content, ct).ConfigureAwait(false);

            await context.SharedMemory.AddDecisionAsync(new Decision
            {
                CreatedBy = context.AgentId,
                Context = task.Content,
                Action = result.FinalAnswer,
                Reasoning = result.Reasoning
            }, ct).ConfigureAwait(false);

            _eventAppender.TryAppendDecisionEvent(context.EventSink, context.AgentId, task, result.Iterations, result.Reasoning, result.FinalAnswer);

            _eventAppender.TryAppendTaskEvent(
                context.EventSink,
                context.AgentId,
                context.AgentRank,
                task,
                EventType.TaskCompleted,
                "Task completed",
                new Dictionary<string, object>
                {
                    ["iterations"] = result.Iterations,
                    ["tool_calls"] = result.ToolCalls.Count
                });

            context.SetStatus(AgentStatus.Idle);

            var basePayload = task.Payload ?? new Dictionary<string, object>();
            var responsePayload = new Dictionary<string, object>(basePayload)
            {
                ["reasoning"] = result.Reasoning,
                ["iterations"] = result.Iterations,
                ["tool_calls"] = result.ToolCalls
            };

            return new AgentMessage
            {
                FromAgentId = context.AgentId,
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
            context.Logger.LogError(ex, "Failed to process task");
            context.SetStatus(AgentStatus.Idle);

            _eventAppender.TryAppendTaskEvent(
                context.EventSink,
                context.AgentId,
                context.AgentRank,
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
                FromAgentId = context.AgentId,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = $"❌ Error: {ex.Message}",
                Payload = new Dictionary<string, object>(task.Payload ?? new Dictionary<string, object>())
            };
        }
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
                var telegramTool = context.ToolRegistry.GetTool("telegram_send");
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
