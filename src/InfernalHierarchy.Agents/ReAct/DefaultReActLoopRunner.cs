using System.Text;
using System.Text.Json;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultReActLoopRunner : IReActLoopRunner
{
    private const int MaxIterations = 5;
    private const string TerminalToolEmailSend = "email_send";
    private const string TerminalToolTelegramSend = "send_telegram";
    private const string ToolGetAgentStatus = "get_agent_status";
    private const string ToolCreateCustomTool = "create_custom_tool";
    private const string ToolWriteMemory = "write_memory";
    private const string ToolFileWrite = "fs_write";
    private const string ToolPythonExec = "python_exec";
    private const string ToolNodeExec = "node_exec";
    private const int MaxFormatRepairAttempts = 2;

    public async Task<ReActLoopResult> RunAsync(ReActLoopContext context, CancellationToken ct)
    {
        var history = new StringBuilder();
        var toolCalls = new List<string>();
        var iterations = 0;
        var consecutiveParseFailures = 0;
        const int maxParseFailures = 3;
        var formatRepairAttempts = 0;

        string? lastToolName = null;
        string? lastToolSignature = null;
        string? lastObservation = null;
        bool lastToolSucceeded = false;
        string? lastAgentStatusObservation = null;
        var agentCountEmailTask = IsAgentCountEmailTask(context.Task, context.Persona.AvailableTools);
        var emailSendSucceeded = false;
        var agentListStatusTask = IsAgentListByNameStatusTask(context.Task, context.Persona.AvailableTools);
        var agentListByNameStatusTask = IsAgentListByNameStatusTask(context.Task);
        var agentListEmailTask = IsAgentListEmailTask(context.Task, context.Persona.AvailableTools);
        var createCustomToolTask = IsCreateCustomToolTask(context.Task, context.Persona.AvailableTools);
        var effectiveAvailableTools = BuildEffectiveAvailableTools(context);
        var forcedInvocation = TryDetectForcedToolInvocation(context.Task, effectiveAvailableTools);

        history.AppendLine($"Task: {context.Task}\n");

        await TryEmitCheckpointAsync(context, new ReActCheckpoint(
            Phase: "plan",
            Label: "task_received",
            Detail: context.Task,
            Iteration: 0,
            OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

        if (forcedInvocation.ToolName is not null)
        {
            history.AppendLine($"Observation: Detected explicit tool invocation request for '{forcedInvocation.ToolName}'.");
        }

        // Deterministic fast-path: if the user explicitly requests invoking a specific tool
        // and supplies a JSON object of parameters, execute the tool immediately and return.
        // This avoids LLM detours/hallucinations and proves end-to-end tool invocation.
        var allowForcedCreateTool = string.Equals(forcedInvocation.ToolName, ToolCreateCustomTool, StringComparison.OrdinalIgnoreCase);

        if ((allowForcedCreateTool || !createCustomToolTask)
            && forcedInvocation.ToolName is not null
            && forcedInvocation.Parameters is not null)
        {
            context.Logger.LogInformation(
                "⚡ Forced tool invocation fast-path: {ToolName}",
                forcedInvocation.ToolName);

            var forcedActionInputText = JsonSerializer.Serialize(forcedInvocation.Parameters);
            context.SetStatus(AgentStatus.ActingWithTool);

            var exec = await context.ActionExecutor.ExecuteAsync(new ActionExecutionContext(
                ToolRegistry: context.ToolRegistry,
                ToolName: forcedInvocation.ToolName,
                ActionInputText: forcedActionInputText,
                ActionInputObject: forcedInvocation.Parameters,
                AgentId: context.AgentId,
                AgentName: context.AgentName,
                AgentRank: context.AgentRank.ToString(),
                AvailableTools: effectiveAvailableTools,
                CancellationToken: ct)).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(exec.ToolCall))
            {
                toolCalls.Add(exec.ToolCall);
            }

            await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                Phase: "execution",
                Label: "forced_tool_invocation",
                Detail: $"tool={forcedInvocation.ToolName};success={exec.Success}",
                Iteration: 1,
                OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

            context.Logger.LogInformation("👁️ {Observation}", exec.Observation);

            await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                Phase: "verification",
                Label: "forced_invocation_completed",
                Detail: exec.Observation,
                Iteration: 1,
                OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

            return new ReActLoopResult(
                FinalAnswer: ObservationToFinalAnswer(exec.Observation),
                Reasoning: $"Forced invocation: {forcedInvocation.ToolName}",
                Iterations: 1,
                ToolCalls: toolCalls);
        }

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
                    effectiveAvailableTools,
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
                    if (context.ReActOptions.UseJsonResponse && formatRepairAttempts < MaxFormatRepairAttempts)
                    {
                        formatRepairAttempts++;
                        var repaired = await TryRepairToJsonAsync(context, effectiveAvailableTools, response, ct).ConfigureAwait(false);

                        if (!string.IsNullOrWhiteSpace(repaired))
                        {
                            history.AppendLine("Observation: Response format incorrect; attempting automatic repair.");
                            history.AppendLine(repaired);

                            if (context.ActionParser.TryParse(repaired, useJsonResponse: true, out parsed))
                            {
                                consecutiveParseFailures = 0;
                            }
                        }
                    }

                    if (parsed.Action is not null && !string.IsNullOrWhiteSpace(parsed.Action))
                    {
                        // Repair succeeded; continue normally.
                    }
                    else
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
                }

                var thought = parsed.Thought;
                var action = parsed.Action;
                var actionInput = parsed.ActionInputText;
                var actionInputObject = parsed.ActionInputObject;

                if (string.Equals(action, TerminalToolEmailSend, StringComparison.OrdinalIgnoreCase)
                    && actionInputObject is not null
                    && TryPrepareEmailSendFromAgentStatus(actionInputObject, lastAgentStatusObservation))
                {
                    actionInput = JsonSerializer.Serialize(actionInputObject);
                }

                context.Logger.LogInformation("💭 Thought: {Thought}", thought);
                context.Logger.LogInformation("⚡ Action: {Action}", action);

                await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                    Phase: "plan",
                    Label: "iteration_planned",
                    Detail: $"action={action}",
                    Iteration: iterations,
                    OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

                consecutiveParseFailures = 0;

                if (agentCountEmailTask
                    && string.Equals(action, TerminalToolEmailSend, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(lastAgentStatusObservation))
                {
                    history.AppendLine("Observation: For agent count emails: call get_agent_status first, then call email_send with numeric total_agents / occupied_agents / idle_agents.");
                    continue;
                }

                if ((agentListStatusTask || agentListByNameStatusTask || agentListEmailTask)
                    && string.IsNullOrWhiteSpace(lastAgentStatusObservation)
                    && !string.Equals(action, ToolGetAgentStatus, StringComparison.OrdinalIgnoreCase))
                {
                    // Deterministic enforcement: for this intent, always obtain get_agent_status first.
                    // Do not rely on the model choosing the correct tool.
                    history.AppendLine("Observation: Forcing get_agent_status to fulfill this request deterministically.");
                    action = ToolGetAgentStatus;
                    actionInput = "{}";
                    actionInputObject = new Dictionary<string, object>();
                }

                if (createCustomToolTask
                    && !string.Equals(action, ToolCreateCustomTool, StringComparison.OrdinalIgnoreCase))
                {
                    // Deterministic enforcement: when the user asks to create a custom tool,
                    // do not let the model answer with generic advice or detour to other tools.
                    history.AppendLine("Observation: Forcing create_custom_tool to fulfill this request deterministically.");
                    action = ToolCreateCustomTool;
                    actionInputObject = new Dictionary<string, object>
                    {
                        ["tool_name"] = DeriveStableCustomToolName(context.Task),
                        ["description"] = context.Task
                    };
                    actionInput = JsonSerializer.Serialize(actionInputObject);
                }

                if (!createCustomToolTask
                    && forcedInvocation.ToolName is not null
                    && forcedInvocation.Parameters is not null
                    && !string.Equals(action, forcedInvocation.ToolName, StringComparison.OrdinalIgnoreCase))
                {
                    history.AppendLine($"Observation: Forcing tool invocation '{forcedInvocation.ToolName}' as explicitly requested.");
                    action = forcedInvocation.ToolName;
                    actionInputObject = forcedInvocation.Parameters;
                    actionInput = JsonSerializer.Serialize(actionInputObject);
                }

                if (createCustomToolTask
                    && string.Equals(action, ToolCreateCustomTool, StringComparison.OrdinalIgnoreCase))
                {
                    // Hardening: the model often calls create_custom_tool with a non-matching schema
                    // (e.g., base_url/endpoint/query_params) which causes missing description/requirement.
                    // Coerce into the minimum required shape while preserving any provided tool_name.
                    actionInputObject ??= new Dictionary<string, object>();

                    static bool HasNonEmpty(Dictionary<string, object> dict, string key)
                        => dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v?.ToString());

                    if (!HasNonEmpty(actionInputObject, "requirement") && !HasNonEmpty(actionInputObject, "description"))
                    {
                        actionInputObject["description"] = context.Task;
                    }

                    if (!HasNonEmpty(actionInputObject, "tool_name"))
                    {
                        actionInputObject["tool_name"] = DeriveStableCustomToolName(context.Task);
                    }
                    else
                    {
                        var name = actionInputObject["tool_name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(name)
                            && !name.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
                        {
                            actionInputObject["tool_name"] = DeriveStableCustomToolName(context.Task);
                        }
                    }

                    // Hint the tool generator to use the deterministic template.
                    if (!HasNonEmpty(actionInputObject, "template"))
                    {
                        actionInputObject["template"] = "http_get_json";
                    }

                    actionInput = JsonSerializer.Serialize(actionInputObject);
                }

                static string DeriveStableCustomToolName(string task)
                {
                    if (task.Contains("la cale", StringComparison.OrdinalIgnoreCase)
                        || task.Contains("lacale", StringComparison.OrdinalIgnoreCase))
                    {
                        return "custom_lacale_api";
                    }

                    return "custom_http_get_json";
                }

                if (action.Contains("FINAL_ANSWER", StringComparison.OrdinalIgnoreCase))
                {
                    if (createCustomToolTask)
                    {
                        history.AppendLine("Observation: This task requires creating a custom tool. Call create_custom_tool before FINAL_ANSWER.");
                        continue;
                    }

                    if (agentCountEmailTask && !emailSendSucceeded)
                    {
                        history.AppendLine("Observation: This task requires sending an email. Call email_send (after get_agent_status) and only then provide FINAL_ANSWER.");
                        continue;
                    }

                    if (agentListEmailTask && !emailSendSucceeded)
                    {
                        history.AppendLine("Observation: Cette demande requiert l’envoi d’un email. Appelle get_agent_status puis email_send avant de donner FINAL_ANSWER.");
                        continue;
                    }

                    await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                        Phase: "verification",
                        Label: "final_answer_ready",
                        Detail: actionInput,
                        Iteration: iterations,
                        OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

                    return new ReActLoopResult(
                        FinalAnswer: actionInput,
                        Reasoning: thought,
                        Iterations: iterations,
                        ToolCalls: toolCalls);
                }

                var toolSignature = BuildToolSignature(action, actionInput, actionInputObject);
                if (lastToolSucceeded
                    && string.Equals(action, lastToolName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(toolSignature, lastToolSignature, StringComparison.Ordinal))
                {
                    // Avoid tight loops where the model repeats the exact same successful tool call.
                    // Reuse the previous observation so the next LLM iteration has new context.
                    var reused = lastObservation ?? "Observation: (duplicate tool call suppressed; previous observation unavailable)";
                    history.AppendLine("Observation: Duplicate tool call suppressed; reusing previous observation.");
                    history.AppendLine(reused);
                    context.Logger.LogDebug("🔁 Suppressed duplicate tool call: {Tool}", action);
                    continue;
                }

                try
                {
                    context.SetStatus(AgentStatus.ActingWithTool);

                    await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                        Phase: "execution",
                        Label: "tool_execution_started",
                        Detail: action,
                        Iteration: iterations,
                        OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

                    var exec = await context.ActionExecutor.ExecuteAsync(new ActionExecutionContext(
                        ToolRegistry: context.ToolRegistry,
                        ToolName: action,
                        ActionInputText: actionInput,
                        ActionInputObject: actionInputObject,
                        AgentId: context.AgentId,
                        AgentName: context.AgentName,
                        AgentRank: context.AgentRank.ToString(),
                        AvailableTools: effectiveAvailableTools,
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

                    await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                        Phase: "execution",
                        Label: "tool_execution_completed",
                        Detail: $"tool={action};success={exec.Success}",
                        Iteration: iterations,
                        OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

                    lastToolName = action;
                    lastToolSignature = toolSignature;
                    lastObservation = exec.Observation;
                    lastToolSucceeded = exec.Success;

                    if (exec.Success && string.Equals(action, ToolGetAgentStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        lastAgentStatusObservation = exec.Observation;

                        if (agentListEmailTask && !emailSendSucceeded
                            && TryRenderAgentListByNameAndStatus(exec.Observation, out var renderedListForEmail))
                        {
                            try
                            {
                                var emailParams = new Dictionary<string, object>
                                {
                                    ["to"] = "Email:DefaultTo",
                                    ["subject"] = "Liste des agents (nom — statut)",
                                    ["body"] = renderedListForEmail,
                                    ["timestamp"] = DateTime.UtcNow.ToString("O")
                                };

                                var emailInputText = JsonSerializer.Serialize(emailParams);
                                var emailExec = await context.ActionExecutor.ExecuteAsync(new ActionExecutionContext(
                                    ToolRegistry: context.ToolRegistry,
                                    ToolName: TerminalToolEmailSend,
                                    ActionInputText: emailInputText,
                                    ActionInputObject: emailParams,
                                    AgentId: context.AgentId,
                                    AgentName: context.AgentName,
                                    AgentRank: context.AgentRank.ToString(),
                                    AvailableTools: context.Persona.AvailableTools,
                                    CancellationToken: ct)).ConfigureAwait(false);

                                if (!string.IsNullOrWhiteSpace(emailExec.ToolCall))
                                {
                                    toolCalls.Add(emailExec.ToolCall);
                                }

                                history.AppendLine(emailExec.Observation);
                                context.Logger.LogInformation("👁️ {Observation}", emailExec.Observation);

                                if (emailExec.Success)
                                {
                                    emailSendSucceeded = true;
                                    await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                                        Phase: "verification",
                                        Label: "terminal_side_effect_completed",
                                        Detail: TerminalToolEmailSend,
                                        Iteration: iterations,
                                        OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);
                                    return new ReActLoopResult(
                                        FinalAnswer: "C’est fait — l’email a bien été envoyé.",
                                        Reasoning: thought,
                                        Iterations: iterations,
                                        ToolCalls: toolCalls);
                                }
                            }
                            catch (Exception ex)
                            {
                                history.AppendLine($"Observation: Tool execution threw exception - {ex.Message}");
                                context.Logger.LogError(ex, "Tool {Tool} execution threw exception", TerminalToolEmailSend);
                            }
                        }

                        if (agentListStatusTask && TryFormatAgentNameStatusList(exec.Observation, out var listText))
                        {
                            return new ReActLoopResult(
                                FinalAnswer: listText,
                                Reasoning: thought,
                                Iterations: iterations,
                                ToolCalls: toolCalls);
                        }

                        if (agentListByNameStatusTask && TryRenderAgentListByNameAndStatus(exec.Observation, out var renderedList))
                        {
                            return new ReActLoopResult(
                                FinalAnswer: renderedList,
                                Reasoning: thought,
                                Iterations: iterations,
                                ToolCalls: toolCalls);
                        }
                    }

                    if (exec.Success && string.Equals(action, TerminalToolEmailSend, StringComparison.OrdinalIgnoreCase))
                    {
                        emailSendSucceeded = true;

                        await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                            Phase: "verification",
                            Label: "terminal_side_effect_completed",
                            Detail: TerminalToolEmailSend,
                            Iteration: iterations,
                            OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

                        // email_send is an external side effect; always stop after success to avoid duplicates.
                        return new ReActLoopResult(
                            FinalAnswer: "C’est fait — l’email a bien été envoyé.",
                            Reasoning: thought,
                            Iterations: iterations,
                            ToolCalls: toolCalls);
                    }

                    if (exec.Success && IsTerminalTool(action, context.ReActOptions))
                    {
                        context.Logger.LogInformation("✅ Terminal tool '{Tool}' succeeded; stopping ReAct loop", action);

                        await TryEmitCheckpointAsync(context, new ReActCheckpoint(
                            Phase: "verification",
                            Label: "terminal_tool_completed",
                            Detail: action,
                            Iteration: iterations,
                            OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

                        var finalAnswer = string.Equals(action, TerminalToolEmailSend, StringComparison.OrdinalIgnoreCase)
                            ? "C’est fait — l’email a bien été envoyé."
                            : ObservationToFinalAnswer(exec.Observation);

                        return new ReActLoopResult(
                            FinalAnswer: finalAnswer,
                            Reasoning: thought,
                            Iterations: iterations,
                            ToolCalls: toolCalls);
                    }

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

        await TryEmitCheckpointAsync(context, new ReActCheckpoint(
            Phase: "verification",
            Label: "max_iterations_reached",
            Detail: $"max_iterations={MaxIterations}",
            Iteration: iterations,
            OccurredAtUtc: DateTime.UtcNow), ct).ConfigureAwait(false);

        return new ReActLoopResult(
            FinalAnswer: $"Task incomplete after {MaxIterations} iterations. Partial progress:\n{history}",
            Reasoning: "Reached maximum iteration limit",
            Iterations: iterations,
            ToolCalls: toolCalls);
    }

    private static async Task TryEmitCheckpointAsync(ReActLoopContext context, ReActCheckpoint checkpoint, CancellationToken ct)
    {
        if (context.EmitCheckpoint is null)
        {
            return;
        }

        try
        {
            await context.EmitCheckpoint(checkpoint, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Logger.LogDebug(ex, "Failed to emit ReAct checkpoint {Label}", checkpoint.Label);
        }
    }

    private static string BuildToolSignature(string toolName, string actionInputText, Dictionary<string, object>? actionInputObject)
    {
        // Duplicate-suppression signature should be stable across superficial formatting differences
        // and across "empty" JSON inputs.
        if (actionInputObject is not null)
        {
            return CanonicalizeToolSignature(toolName, actionInputObject);
        }

        if (string.IsNullOrWhiteSpace(actionInputText))
        {
            return CanonicalizeToolSignature(toolName, new Dictionary<string, object>());
        }

        var trimmed = actionInputText.Trim();
        if (trimmed == "{}")
        {
            return CanonicalizeToolSignature(toolName, new Dictionary<string, object>());
        }

        // Best-effort parse JSON objects for stable ordering.
        if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(doc.RootElement.GetRawText());
                    if (dict is not null)
                    {
                        return CanonicalizeToolSignature(toolName, dict);
                    }
                }
            }
            catch (JsonException)
            {
                // fall through to raw
            }
        }

        return trimmed;
    }

    private static string CanonicalizeToolSignature(string toolName, Dictionary<string, object> parameters)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return StableKeyValueString(parameters);
        }

        if (string.Equals(toolName, ToolGetAgentStatus, StringComparison.OrdinalIgnoreCase))
        {
            // Treat empty input and default query as equivalent to reduce wasteful repeated calls.
            var canonical = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (parameters.TryGetValue("query", out var queryObj))
            {
                canonical["query"] = CoerceString(queryObj)?.Trim() ?? "all";
            }
            else
            {
                canonical["query"] = "all";
            }

            // Ignore common injected/meta keys if the model includes them.
            // (Executor injects these for agent tools, but they're not semantically relevant for the query.)
            return StableKeyValueString(canonical);
        }

        if (string.Equals(toolName, TerminalToolEmailSend, StringComparison.OrdinalIgnoreCase))
        {
            // Prevent spam: treat email_send calls with the same meaningful content as duplicates,
            // even if the model varies formatting or injects non-semantic metadata like timestamp.
            var canonical = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (parameters.TryGetValue("to", out var toObj)) canonical["to"] = CoerceString(toObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("recipient", out var recipientObj) && !canonical.ContainsKey("to")) canonical["to"] = CoerceString(recipientObj)?.Trim() ?? string.Empty;

            if (parameters.TryGetValue("subject", out var subjectObj)) canonical["subject"] = CoerceString(subjectObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("subjeect", out var subjectTypoObj) && !canonical.ContainsKey("subject")) canonical["subject"] = CoerceString(subjectTypoObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("title", out var titleObj) && !canonical.ContainsKey("subject")) canonical["subject"] = CoerceString(titleObj)?.Trim() ?? string.Empty;

            if (parameters.TryGetValue("body", out var bodyObj)) canonical["body"] = CoerceString(bodyObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("message", out var messageObj) && !canonical.ContainsKey("body")) canonical["body"] = CoerceString(messageObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("content", out var contentObj) && !canonical.ContainsKey("body")) canonical["body"] = CoerceString(contentObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("text", out var textObj) && !canonical.ContainsKey("body")) canonical["body"] = CoerceString(textObj)?.Trim() ?? string.Empty;

            if (parameters.TryGetValue("cc", out var ccObj)) canonical["cc"] = CoerceString(ccObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("bcc", out var bccObj)) canonical["bcc"] = CoerceString(bccObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("reply_to", out var replyToObj)) canonical["reply_to"] = CoerceString(replyToObj)?.Trim() ?? string.Empty;
            if (parameters.TryGetValue("is_html", out var isHtmlObj)) canonical["is_html"] = StableValueString(isHtmlObj);

            return StableKeyValueString(canonical);
        }

        if (string.Equals(toolName, TerminalToolTelegramSend, StringComparison.OrdinalIgnoreCase))
        {
            var canonical = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            canonical["chat_id"] = FirstNonEmpty(parameters, "chat_id", "chatId", "telegram_chat_id", "telegramChatId") ?? string.Empty;
            canonical["text"] = FirstNonEmpty(parameters, "text", "message", "content") ?? string.Empty;

            return StableKeyValueString(canonical);
        }

        if (string.Equals(toolName, ToolWriteMemory, StringComparison.OrdinalIgnoreCase))
        {
            var canonical = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var type = FirstNonEmpty(parameters, "type") ?? string.Empty;
            canonical["type"] = type.Trim().ToLowerInvariant();
            canonical["agent_id"] = FirstNonEmpty(parameters, "agent_id") ?? string.Empty;

            if (type.Equals("decision", StringComparison.OrdinalIgnoreCase))
            {
                canonical["context"] = FirstNonEmpty(parameters, "context") ?? string.Empty;
                canonical["action"] = FirstNonEmpty(parameters, "action") ?? string.Empty;
                canonical["reasoning"] = FirstNonEmpty(parameters, "reasoning") ?? string.Empty;
            }
            else if (type.Equals("fact", StringComparison.OrdinalIgnoreCase))
            {
                canonical["category"] = FirstNonEmpty(parameters, "category") ?? "general";
                canonical["content"] = FirstNonEmpty(parameters, "content") ?? string.Empty;
                canonical["source"] = FirstNonEmpty(parameters, "source") ?? "agent";
                canonical["confidence"] = StableValueString(parameters.TryGetValue("confidence", out var confidence) ? confidence : 1.0d);
                canonical["visibility"] = FirstNonEmpty(parameters, "visibility") ?? "Private";
                canonical["min_rank"] = FirstNonEmpty(parameters, "min_rank") ?? string.Empty;
                canonical["shared_with"] = CanonicalizeDelimitedString(FirstNonEmpty(parameters, "shared_with"));
            }
            else if (type.Equals("task", StringComparison.OrdinalIgnoreCase))
            {
                canonical["description"] = FirstNonEmpty(parameters, "description") ?? string.Empty;
                canonical["assigned_to"] = FirstNonEmpty(parameters, "assigned_to") ?? canonical["agent_id"].ToString() ?? string.Empty;
            }

            return StableKeyValueString(canonical);
        }

        if (string.Equals(toolName, ToolCreateCustomTool, StringComparison.OrdinalIgnoreCase))
        {
            var canonical = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool_name"] = FirstNonEmpty(parameters, "tool_name") ?? string.Empty,
                ["requirement"] = FirstNonEmpty(parameters, "requirement", "description") ?? string.Empty,
                ["template"] = FirstNonEmpty(parameters, "template") ?? string.Empty,
                ["overwrite"] = StableValueString(FirstNonNull(parameters, "overwrite", "force", "overwrite_existing", "overwriteExisting") ?? false)
            };

            return StableKeyValueString(canonical);
        }

        if (string.Equals(toolName, ToolFileWrite, StringComparison.OrdinalIgnoreCase))
        {
            var canonical = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["path"] = FirstNonEmpty(parameters, "path") ?? string.Empty,
                ["content"] = FirstNonEmpty(parameters, "content") ?? string.Empty,
                ["overwrite"] = StableValueString(FirstNonNull(parameters, "overwrite") ?? false)
            };

            return StableKeyValueString(canonical);
        }

        if (string.Equals(toolName, ToolPythonExec, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ToolNodeExec, StringComparison.OrdinalIgnoreCase))
        {
            var canonical = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = FirstNonEmpty(parameters, "code") ?? string.Empty,
                ["working_dir"] = FirstNonEmpty(parameters, "working_dir") ?? string.Empty,
                ["timeout_ms"] = StableValueString(FirstNonNull(parameters, "timeout_ms") ?? string.Empty),
                ["args"] = StableValueString(FirstNonNull(parameters, "args") ?? string.Empty)
            };

            return StableKeyValueString(canonical);
        }

        // Default: stable ordering of all keys.
        return StableKeyValueString(parameters);
    }

    private static object? FirstNonNull(Dictionary<string, object> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(Dictionary<string, object> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            var str = CoerceString(value)?.Trim();
            if (!string.IsNullOrWhiteSpace(str))
            {
                return str;
            }
        }

        return null;
    }

    private static string CanonicalizeDelimitedString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(",",
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(part => part, StringComparer.OrdinalIgnoreCase));
    }

    private static string StableKeyValueString(Dictionary<string, object> parameters)
    {
        if (parameters.Count == 0)
        {
            return "{}";
        }

        var ordered = parameters
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => $"{kvp.Key}={StableValueString(kvp.Value)}");

        return "{" + string.Join(";", ordered) + "}";
    }

    private static string StableValueString(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return value switch
        {
            string s => s.Trim(),
            JsonElement je => je.ValueKind == JsonValueKind.String ? (je.GetString() ?? string.Empty) : je.GetRawText(),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool IsTerminalTool(string toolName, ReActOptions options)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        var terminalTools = options.TerminalTools;
        if (terminalTools is null || terminalTools.Length == 0)
        {
            return false;
        }

        return terminalTools.Any(t => string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ObservationToFinalAnswer(string? observation)
    {
        if (string.IsNullOrWhiteSpace(observation))
        {
            return "Done.";
        }

        const string prefix = "Observation:";
        if (observation.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return observation[prefix.Length..].Trim();
        }

        return observation.Trim();
    }

    private static bool IsCreateCustomToolTask(string task, IReadOnlyCollection<string> availableTools)
    {
        if (availableTools is null || !availableTools.Contains(ToolCreateCustomTool, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            return false;
        }

        var t = task.ToLowerInvariant();

        // French + English; keep it simple and conservative.
        var wantsCreate = t.Contains("create") || t.Contains("crée") || t.Contains("cree") || t.Contains("génère") || t.Contains("genere");
        var mentionsTool = t.Contains("tool") || t.Contains("outil") || t.Contains("custom tool") || t.Contains("custom") || t.Contains("outil custom");

        return wantsCreate && mentionsTool;
    }

    private static IReadOnlyCollection<string> BuildEffectiveAvailableTools(ReActLoopContext context)
    {
        var set = new HashSet<string>(context.Persona.AvailableTools ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        if (context.AgentRank == AgentRank.Supreme)
        {
            foreach (var tool in context.ToolRegistry.GetAllTools())
            {
                if (tool?.Name is null)
                {
                    continue;
                }

                if (tool.Name.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(tool.Name);
                }
            }
        }

        return set.ToArray();
    }

    private sealed record ForcedToolInvocation(string? ToolName, Dictionary<string, object>? Parameters);

    private static ForcedToolInvocation TryDetectForcedToolInvocation(string task, IReadOnlyCollection<string> effectiveAvailableTools)
    {
        if (string.IsNullOrWhiteSpace(task) || effectiveAvailableTools.Count == 0)
        {
            return new ForcedToolInvocation(null, null);
        }

        var t = task;
        var lower = task.ToLowerInvariant();

        // Only trigger when the user is clearly asking to run/call a tool.
        var asksToInvoke = lower.Contains("use tool")
                          || lower.Contains("call tool")
                          || lower.Contains("invoke")
                          || lower.Contains("run tool")
                          || lower.Contains("execute tool")
                          || lower.Contains("utilise l'outil")
                          || lower.Contains("utilise loutil")
                          || lower.Contains("appelle l'outil")
                          || lower.Contains("appelle loutil")
                          || lower.Contains("exécute")
                          || lower.Contains("execute");

        if (!asksToInvoke)
        {
            return new ForcedToolInvocation(null, null);
        }

        string? matchedTool = null;
        foreach (var tool in effectiveAvailableTools)
        {
            if (string.IsNullOrWhiteSpace(tool))
            {
                continue;
            }

            if (lower.Contains(tool.ToLowerInvariant()))
            {
                matchedTool = tool;
                break;
            }
        }

        if (matchedTool is null)
        {
            return new ForcedToolInvocation(null, null);
        }

        // Require an explicit JSON object in the task for deterministic parameters.
        if (!TryExtractFirstJsonObject(t, out var json))
        {
            return new ForcedToolInvocation(null, null);
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dict is null
                ? new ForcedToolInvocation(null, null)
                : new ForcedToolInvocation(matchedTool, dict);
        }
        catch
        {
            return new ForcedToolInvocation(null, null);
        }
    }

    private static async Task<string?> TryRepairToJsonAsync(ReActLoopContext context, IReadOnlyCollection<string> availableTools, string rawResponse, CancellationToken ct)
    {
        try
        {
            var tools = availableTools.Count == 0
                ? "(none)"
                : string.Join(", ", availableTools);

            var repairSystem = "You are a strict formatter. Output ONLY a single JSON object with keys thought, action, actionInput. No markdown, no code fences, no extra text.";
            var repairPrompt = $$"""
                Convert the following model output into the required JSON response schema.

                Requirements:
                - Output MUST be valid JSON.
                - action MUST be FINAL_ANSWER or one of these tools: {{tools}}
                - If action is a tool: actionInput MUST be an object (tool parameters)
                - If action is FINAL_ANSWER: actionInput MUST be a string

                Text to convert:
                {{rawResponse}}
                """;

            return await context.LlmClient.GetCompletionAsync(repairSystem, repairPrompt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Logger.LogDebug(ex, "Format repair attempt failed");
            return null;
        }
    }

    private static bool TryPrepareEmailSendFromAgentStatus(Dictionary<string, object> actionInputObject, string? lastAgentStatusObservation)
    {
        var changed = false;

        if (actionInputObject.TryGetValue("to", out var toObj))
        {
            var to = CoerceString(toObj);
            if (!string.IsNullOrWhiteSpace(to) && LooksLikeHttpCorrelationId(to))
            {
                // Let EmailNotificationTool fall back to Email:DefaultTo.
                actionInputObject.Remove("to");
                changed = true;
            }
        }

        if (!actionInputObject.TryGetValue("body", out var bodyObj))
        {
            return changed;
        }

        var body = CoerceString(bodyObj);
        if (string.IsNullOrWhiteSpace(body))
        {
            return changed;
        }

        if (!LooksLikePlaceholderTemplate(body))
        {
            return changed;
        }

        if (!TryGetAgentCounts(lastAgentStatusObservation, out var total, out var occupied, out var idle))
        {
            return changed;
        }

        var rendered = body
            .Replace("${total_agents}", total.ToString(), StringComparison.Ordinal)
            .Replace("${occupied_agents}", occupied.ToString(), StringComparison.Ordinal)
            .Replace("${idle_agents}", idle.ToString(), StringComparison.Ordinal)
            .Replace("{{total_agents}}", total.ToString(), StringComparison.Ordinal)
            .Replace("{{occupied_agents}}", occupied.ToString(), StringComparison.Ordinal)
            .Replace("{{idle_agents}}", idle.ToString(), StringComparison.Ordinal);

        // If the model used placeholders we don't recognize, overwrite with a safe concrete summary.
        if (LooksLikePlaceholderTemplate(rendered))
        {
            rendered = $"D\u00e9compte des agents actifs: total={total}, occup\u00e9s={occupied}, inactifs={idle}.";
        }

        actionInputObject["body"] = rendered;
        return true;
    }

    private static bool IsAgentCountEmailTask(string task, IReadOnlyCollection<string> availableTools)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return false;
        }

        if (!availableTools.Contains(ToolGetAgentStatus, StringComparer.OrdinalIgnoreCase)
            || !availableTools.Contains(TerminalToolEmailSend, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var t = task;
        if (!t.Contains("mail", StringComparison.OrdinalIgnoreCase)
            && !t.Contains("email", StringComparison.OrdinalIgnoreCase)
            && !t.Contains("e-mail", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!t.Contains("agent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return t.Contains("decompte", StringComparison.OrdinalIgnoreCase)
            || t.Contains("décompte", StringComparison.OrdinalIgnoreCase)
            || t.Contains("count", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAgentListByNameStatusTask(string task, IReadOnlyCollection<string> availableTools)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return false;
        }

        if (!availableTools.Contains(ToolGetAgentStatus, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var t = task;

        // Telegram phrasing examples: "liste les tous par leur nom et leur status"
        var asksList = t.Contains("liste", StringComparison.OrdinalIgnoreCase)
            || t.Contains("list", StringComparison.OrdinalIgnoreCase)
            || t.Contains("tous", StringComparison.OrdinalIgnoreCase)
            || t.Contains("all", StringComparison.OrdinalIgnoreCase);

        if (!asksList)
        {
            return false;
        }

        var asksName = t.Contains("nom", StringComparison.OrdinalIgnoreCase)
            || t.Contains("name", StringComparison.OrdinalIgnoreCase);

        var asksStatus = t.Contains("status", StringComparison.OrdinalIgnoreCase)
            || t.Contains("statut", StringComparison.OrdinalIgnoreCase);

        // We intentionally do not require the literal word "agent" here.
        // The shortcut only triggers after a successful get_agent_status tool call,
        // so the model has already committed to the "agents" interpretation.
        return asksName && asksStatus;
    }

    private static bool IsAgentListEmailTask(string task, IReadOnlyCollection<string> availableTools)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return false;
        }

        if (!availableTools.Contains(ToolGetAgentStatus, StringComparer.OrdinalIgnoreCase)
            || !availableTools.Contains(TerminalToolEmailSend, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var t = task;
        var mentionsList = t.Contains("liste", StringComparison.OrdinalIgnoreCase)
            || t.Contains("list", StringComparison.OrdinalIgnoreCase)
            || t.Contains("tous", StringComparison.OrdinalIgnoreCase)
            || t.Contains("all", StringComparison.OrdinalIgnoreCase);

        if (!mentionsList)
        {
            return false;
        }

        var mentionsEmail = t.Contains("mail", StringComparison.OrdinalIgnoreCase)
            || t.Contains("email", StringComparison.OrdinalIgnoreCase)
            || t.Contains("e-mail", StringComparison.OrdinalIgnoreCase);

        return mentionsEmail;
    }

    private static bool TryFormatAgentNameStatusList(string? agentStatusObservation, out string formatted)
    {
        formatted = string.Empty;

        if (string.IsNullOrWhiteSpace(agentStatusObservation))
        {
            return false;
        }

        var text = agentStatusObservation.Trim();
        const string prefix = "Observation:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[prefix.Length..].Trim();
        }

        if (!TryExtractFirstJsonObject(text, out var json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("agents", out var agentsProp) || agentsProp.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var lines = new List<string>();
            foreach (var agent in agentsProp.EnumerateArray())
            {
                if (agent.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = agent.TryGetProperty("name", out var nameProp) ? (nameProp.GetString() ?? string.Empty) : string.Empty;
                var status = agent.TryGetProperty("status", out var statusProp) ? (statusProp.GetString() ?? string.Empty) : string.Empty;

                name = name.Trim();
                status = status.Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(status))
                {
                    status = "(inconnu)";
                }

                lines.Add($"- {name} — {status}");
            }

            if (lines.Count == 0)
            {
                return false;
            }

            formatted = "Voici la liste des agents (nom — statut) :\n" + string.Join("\n", lines);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeHttpCorrelationId(string value)
        => value.Trim().StartsWith("http-", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePlaceholderTemplate(string value)
    {
        var v = value.Trim();
        return v.Contains("${", StringComparison.Ordinal)
            || v.Contains("{{", StringComparison.Ordinal)
            || v.Contains("}}", StringComparison.Ordinal);
    }

    private static string? CoerceString(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => value.ToString()
        };
    }

    private static bool TryGetAgentCounts(string? agentStatusObservation, out int total, out int occupied, out int idle)
    {
        total = 0;
        occupied = 0;
        idle = 0;

        if (string.IsNullOrWhiteSpace(agentStatusObservation))
        {
            return false;
        }

        var text = agentStatusObservation.Trim();
        const string prefix = "Observation:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[prefix.Length..].Trim();
        }

        if (!TryExtractFirstJsonObject(text, out var json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("total_agents", out var totalProp)
                || !root.TryGetProperty("occupied_agents", out var occupiedProp)
                || !root.TryGetProperty("idle_agents", out var idleProp))
            {
                return false;
            }

            total = totalProp.GetInt32();
            occupied = occupiedProp.GetInt32();
            idle = idleProp.GetInt32();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsAgentListByNameStatusTask(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return false;
        }

        // Match the common Telegram phrasing: "liste les tous par leur nom et leur status/statut".
        var t = task;
        var mentionsList = t.Contains("liste", StringComparison.OrdinalIgnoreCase) || t.Contains("list", StringComparison.OrdinalIgnoreCase);
        var mentionsName = t.Contains("nom", StringComparison.OrdinalIgnoreCase) || t.Contains("name", StringComparison.OrdinalIgnoreCase);
        var mentionsStatus = t.Contains("status", StringComparison.OrdinalIgnoreCase) || t.Contains("statut", StringComparison.OrdinalIgnoreCase);

        // Same as above: do not require the literal word "agent".
        return mentionsList && mentionsName && mentionsStatus;
    }

    private static bool TryRenderAgentListByNameAndStatus(string? agentStatusObservation, out string rendered)
    {
        rendered = string.Empty;

        if (string.IsNullOrWhiteSpace(agentStatusObservation))
        {
            return false;
        }

        var text = agentStatusObservation.Trim();
        const string prefix = "Observation:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[prefix.Length..].Trim();
        }

        if (!TryExtractFirstJsonObject(text, out var json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("agents", out var agentsProp) || agentsProp.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var lines = new List<string>();
            foreach (var agent in agentsProp.EnumerateArray())
            {
                if (agent.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = agent.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString()
                    : null;

                var status = agent.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String
                    ? statusProp.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                lines.Add(string.IsNullOrWhiteSpace(status)
                    ? $"- {name}"
                    : $"- {name} — {status}");
            }

            if (lines.Count == 0)
            {
                return false;
            }

            rendered = "Voici la liste des agents (nom — statut) :\n" + string.Join("\n", lines);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractFirstJsonObject(string text, out string json)
    {
        json = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();

        if (candidate.StartsWith("{", StringComparison.Ordinal))
        {
            json = candidate;
            return true;
        }

        var start = -1;
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < candidate.Length; i++)
        {
            var c = candidate[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                {
                    start = i;
                }

                depth++;
                continue;
            }

            if (c == '}' && depth > 0)
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    json = candidate.Substring(start, i - start + 1);
                    return true;
                }
            }
        }

        return false;
    }
}
