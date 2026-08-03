using System.Text.RegularExpressions;
using System.Text.Json;
using System.Diagnostics;
using InfernalHierarchy.Core.Serialization;

namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultReActTaskProcessor : IReActTaskProcessor
{
    private const int MaxIterations = 5;

    private readonly IRagContextEnricher _ragContextEnricher;
    private readonly IAgentEventAppender _eventAppender;
    private readonly ICapabilityGapAnalyzer _capabilityGapAnalyzer;
    private readonly ICapabilityRemediationOrchestrator _capabilityRemediationOrchestrator;

    public DefaultReActTaskProcessor(
        IRagContextEnricher ragContextEnricher,
        IAgentEventAppender eventAppender,
        ICapabilityGapAnalyzer? capabilityGapAnalyzer = null,
        ICapabilityRemediationOrchestrator? capabilityRemediationOrchestrator = null)
    {
        _ragContextEnricher = ragContextEnricher;
        _eventAppender = eventAppender;
        _capabilityGapAnalyzer = capabilityGapAnalyzer ?? new DefaultCapabilityGapAnalyzer();
        _capabilityRemediationOrchestrator = capabilityRemediationOrchestrator ?? new DefaultCapabilityRemediationOrchestrator();
    }

    public async Task<AgentMessage> ProcessAsync(ReActTaskProcessorContext context, AgentMessage task, CancellationToken ct)
    {
        var overlay = context.RuntimeSkillStore?.GetOverlay(context.AgentId, DateTime.UtcNow);
        var effectivePersona = BuildEffectivePersonaForTask(context.Persona, task, overlay);
        var effectiveContext = context with { Persona = effectivePersona };

        _eventAppender.TryAppendTaskEvent(context.EventSink, context.AgentId, context.AgentRank, task, EventType.TaskReceived, "Task received");

        if (IsCollaborationRequest(task))
        {
            return await HandleCollaborationRequestAsync(effectiveContext, task, ct).ConfigureAwait(false);
        }

        if (TryGetTelegramCommand(task, out var command) && (command == "usage" || command == "models"))
        {
            return await HandleTelegramCommandAsync(effectiveContext, command, task, ct).ConfigureAwait(false);
        }

        var sensitiveInputAssessment = SensitiveInputGuard.Assess(task);
        if (sensitiveInputAssessment.RequiresSecretReference)
        {
            TryAppendSensitiveInputGuardEvent(effectiveContext, task, sensitiveInputAssessment.ReasonCode);

            return new AgentMessage
            {
                FromAgentId = effectiveContext.AgentId,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = "Sensitive credentials detected. Use a secret reference (for example secret://mailbox/prod/main) instead of raw login/password.",
                Payload = new Dictionary<string, object>(task.Payload ?? new Dictionary<string, object>())
                {
                    ["capability_gap_state"] = "blocked_by_sensitive_input_guard",
                    ["block_reason_code"] = sensitiveInputAssessment.ReasonCode
                },
                CorrelationId = task.CorrelationId ?? task.Id,
                CausationId = task.Id
            };
        }

        CapabilityGapAnalysisResult? gapAnalysis = null;
        CapabilityRemediationExecutionResult? remediationResult = null;
        Dictionary<string, object> effectivePayload = new(task.Payload ?? new Dictionary<string, object>());

        if (task.Type is MessageType.Task or MessageType.Query or MessageType.Command)
        {
            gapAnalysis = await _capabilityGapAnalyzer
                .AnalyzeAsync(effectiveContext, task, effectiveContext.Persona, ct)
                .ConfigureAwait(false);

            if (gapAnalysis.HasGaps && gapAnalysis.Report is not null)
            {
                var gapWorkflowId = task.CorrelationId ?? task.Id;
                effectivePayload["original_intent"] = task.Content ?? string.Empty;
                effectivePayload["capability_gap_state"] = "capability_gap_detected";
                effectivePayload["capability_gap_block_reason_code"] = gapAnalysis.Report.BlockReasonCode;
                effectivePayload["capability_gap_workflow_id"] = gapWorkflowId;
            }

            if (gapAnalysis.HasGaps)
            {
                if (gapAnalysis.Report is not null && !gapAnalysis.Report.CanAutofix)
                {
                    EmitCapabilityGapDecisionEvent(
                        effectiveContext,
                        task,
                        gapAnalysis,
                        remediation: null,
                        remediationDurationMs: null,
                        workflowState: "capability_gap_policy_blocked",
                        terminalReasonCode: gapAnalysis.Report.BlockReasonCode);

                    effectivePayload["capability_gap_state"] = "capability_gap_policy_blocked";
                    effectivePayload["capability_gap_report"] = JsonSerializer.Serialize(gapAnalysis.Report, JsonDefaults.Web);

                    return new AgentMessage
                    {
                        FromAgentId = effectiveContext.AgentId,
                        ToAgentId = task.FromAgentId,
                        Type = MessageType.Report,
                        Content = $"Capability gap detected but auto-fix is blocked by policy/risk ({gapAnalysis.Report.BlockReasonCode}).",
                        Payload = effectivePayload,
                        CorrelationId = task.CorrelationId ?? task.Id,
                        CausationId = task.Id
                    };
                }

                var remediationStopwatch = Stopwatch.StartNew();
                remediationResult = await _capabilityRemediationOrchestrator
                    .ExecuteAsync(effectiveContext, task, gapAnalysis, ct)
                    .ConfigureAwait(false);
                remediationStopwatch.Stop();

                EmitCapabilityGapDecisionEvent(
                    effectiveContext,
                    task,
                    gapAnalysis,
                    remediationResult,
                    remediationStopwatch.Elapsed.TotalMilliseconds,
                    remediationResult.WorkflowState,
                    remediationResult.TerminalReasonCode);

                if (remediationResult.NewlyAvailableTools.Count > 0)
                {
                    effectivePersona = AppendToolsToPersona(effectiveContext.Persona, remediationResult.NewlyAvailableTools);
                    effectiveContext = effectiveContext with { Persona = effectivePersona };
                }

                var refreshedOverlay = context.RuntimeSkillStore?.GetOverlay(context.AgentId, DateTime.UtcNow);
                effectivePersona = BuildEffectivePersonaForTask(effectiveContext.Persona, task, refreshedOverlay);
                effectiveContext = effectiveContext with { Persona = effectivePersona };

                if (TryResolveSwitchedExecutionProfile(remediationResult, out var switchedProfile, out var switchReasonCode))
                {
                    effectivePayload["execution_profile"] = switchedProfile;
                    effectivePayload["profile"] = switchedProfile;

                    TryAppendExecutionProfileSwitchEvent(
                        effectiveContext,
                        task,
                        switchedProfile,
                        switchReasonCode);
                }

                effectivePayload["capability_gap_state"] = remediationResult.WorkflowState;
                effectivePayload["capability_gap_terminal_reason_code"] = remediationResult.TerminalReasonCode;

                if (string.Equals(remediationResult.WorkflowState, "capability_gap_unresolved_terminal", StringComparison.OrdinalIgnoreCase))
                {
                    effectivePayload["capability_gap_report"] = JsonSerializer.Serialize(gapAnalysis.Report, JsonDefaults.Web);
                    effectivePayload["capability_gap_plan"] = JsonSerializer.Serialize(gapAnalysis.Plan, JsonDefaults.Web);

                    return new AgentMessage
                    {
                        FromAgentId = effectiveContext.AgentId,
                        ToAgentId = task.FromAgentId,
                        Type = MessageType.Report,
                        Content = "Capability gap could not be remediated autonomously. Workflow terminated with audit trace.",
                        Payload = effectivePayload,
                        CorrelationId = task.CorrelationId ?? task.Id,
                        CausationId = task.Id
                    };
                }

                if (remediationResult.ReplayRequested)
                {
                    var alreadyReplayed = IsReplayAlreadyAttempted(effectivePayload);
                    if (alreadyReplayed)
                    {
                        effectivePayload["capability_gap_state"] = "capability_gap_replay_guard_triggered";

                        return new AgentMessage
                        {
                            FromAgentId = effectiveContext.AgentId,
                            ToAgentId = task.FromAgentId,
                            Type = MessageType.Report,
                            Content = "Capability gap remediation succeeded but replay guard prevented duplicate automatic replay.",
                            Payload = effectivePayload,
                            CorrelationId = task.CorrelationId ?? task.Id,
                            CausationId = task.Id
                        };
                    }

                    effectivePayload["capability_gap_replay_attempted"] = true;
                    effectivePayload["capability_gap_state"] = "capability_gap_replay_in_progress";
                }
            }
        }

        var effectiveTask = CloneTaskWithPayload(task, effectivePayload);

        var effectiveTaskContent = ResolveReplayTaskContent(task, effectivePayload);
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
                    Content = response,
                    CorrelationId = task.CorrelationId ?? task.Id,
                    CausationId = task.Id
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
                    Payload = new Dictionary<string, object>(task.Payload ?? new Dictionary<string, object>()),
                    CorrelationId = task.CorrelationId ?? task.Id,
                    CausationId = task.Id
                };
            }
        }

        effectiveContext.SetStatus(AgentStatus.Thinking);
        effectiveContext.Logger.LogInformation("🔥 {AgentName} processing task: {Content}", effectiveContext.AgentName, effectiveTaskContent);

        try
        {
            _eventAppender.TryAppendTaskEvent(effectiveContext.EventSink, effectiveContext.AgentId, effectiveContext.AgentRank, effectiveTask, EventType.TaskStarted, "Task started");

            var baseContext = await effectiveContext.BuildBaseContextAsync(effectiveTask, ct).ConfigureAwait(false);
            var resolvedExecutionProfile = ResolveExecutionProfile(effectiveTask.Payload);
            var planning = ReActTaskComplexityAdvisor.Assess(
                effectiveTaskContent,
                effectiveContext.Persona.AvailableTools,
                resolvedExecutionProfile,
                effectiveContext.ReActOptions);
            var systemContext = await _ragContextEnricher.EnrichAsync(
                baseContext,
                query: effectiveTaskContent,
                agentId: effectiveContext.AgentId,
                agentRank: effectiveContext.AgentRank,
                vectorMemory: effectiveContext.VectorMemory,
                ragOptions: effectiveContext.RagOptions,
                logger: effectiveContext.Logger,
                ct: ct).ConfigureAwait(false);

            systemContext = AppendRuntimeConstraints(systemContext, effectiveContext.Persona, effectiveTask, planning);

            var replayActive = string.Equals(
                effectivePayload.TryGetValue("capability_gap_state", out var gapStateObj) ? gapStateObj?.ToString() : null,
                "capability_gap_replay_in_progress",
                StringComparison.OrdinalIgnoreCase);

            ReActResult result;
            if (replayActive)
            {
                var replayOutcome = await TryRunLoopWithReplayBudgetAsync(
                    effectiveContext,
                    systemContext,
                    effectiveTaskContent,
                    effectiveTask,
                    ct).ConfigureAwait(false);

                if (!replayOutcome.Success)
                {
                    TryAppendReplayOutcomeEvent(
                        effectiveContext,
                        task,
                        status: "failed",
                        replayOutcome.AttemptsUsed,
                        replayOutcome.ErrorMessage);

                    effectiveContext.SetStatus(AgentStatus.Idle);

                    effectivePayload["capability_gap_state"] = "capability_gap_unresolved_terminal";
                    effectivePayload["capability_gap_terminal_reason_code"] = "replay_budget_exhausted";
                    effectivePayload["capability_gap_replay_failure"] = replayOutcome.ErrorMessage ?? "unknown";

                    _eventAppender.TryAppendTaskEvent(
                        effectiveContext.EventSink,
                        effectiveContext.AgentId,
                        effectiveContext.AgentRank,
                        effectiveTask,
                        EventType.TaskFailed,
                        "Task failed after replay budget exhaustion",
                        new Dictionary<string, object>
                        {
                            ["error"] = replayOutcome.ErrorMessage ?? "unknown",
                            ["exception_type"] = "ReplayBudgetExhausted",
                            ["reason_code"] = "replay_budget_exhausted"
                        });

                    return new AgentMessage
                    {
                        FromAgentId = effectiveContext.AgentId,
                        ToAgentId = task.FromAgentId,
                        Type = MessageType.Report,
                        Content = "Capability gap remediation succeeded but replay exhausted retry budget before completion.",
                        Payload = effectivePayload,
                        CorrelationId = effectiveTask.CorrelationId ?? effectiveTask.Id,
                        CausationId = effectiveTask.Id
                    };
                }

                TryAppendReplayOutcomeEvent(
                    effectiveContext,
                    task,
                    status: "success",
                    replayOutcome.AttemptsUsed,
                    note: null);

                result = replayOutcome.Result!;
            }
            else
            {
                result = await RunLoopAsync(effectiveContext, systemContext, effectiveTaskContent, effectiveTask, ct).ConfigureAwait(false);
            }

            await effectiveContext.SharedMemory.AddDecisionAsync(new Decision
            {
                CreatedBy = effectiveContext.AgentId,
                Context = task.Content ?? string.Empty,
                Action = result.FinalAnswer,
                Reasoning = result.Reasoning
            }, ct).ConfigureAwait(false);

            _eventAppender.TryAppendDecisionEvent(effectiveContext.EventSink, effectiveContext.AgentId, task, result.Iterations, result.Reasoning, result.FinalAnswer);

            _eventAppender.TryAppendTaskEvent(
                effectiveContext.EventSink,
                effectiveContext.AgentId,
                effectiveContext.AgentRank,
                effectiveTask,
                EventType.TaskCompleted,
                "Task completed",
                new Dictionary<string, object>
                {
                    ["iterations"] = result.Iterations,
                    ["tool_calls"] = result.ToolCalls.Count,
                    ["task_complexity"] = planning.Complexity.ToString(),
                    ["iteration_budget"] = planning.IterationBudget,
                    ["recommended_parallel_branches"] = planning.RecommendedParallelBranches
                });

            effectiveContext.SetStatus(AgentStatus.Idle);

            var basePayload = effectiveTask.Payload ?? new Dictionary<string, object>();
            var responsePayload = new Dictionary<string, object>(basePayload)
            {
                ["reasoning"] = result.Reasoning,
                ["iterations"] = result.Iterations,
                ["tool_calls"] = result.ToolCalls,
                ["task_complexity"] = planning.Complexity.ToString(),
                ["iteration_budget"] = planning.IterationBudget,
                ["recommended_parallel_branches"] = planning.RecommendedParallelBranches,
                ["complexity_reason_code"] = planning.ReasonCode
            };

            if (gapAnalysis is not null && gapAnalysis.HasGaps)
            {
                responsePayload["capability_gap_analysis"] = JsonSerializer.Serialize(new
                {
                    report = gapAnalysis.Report,
                    plan = gapAnalysis.Plan,
                    gaps = gapAnalysis.Gaps.Select(g => new
                    {
                        capability = g.Capability,
                        reason_code = g.ReasonCode,
                        description = g.Description,
                        blocked_by_profile = g.BlockedByProfile,
                        suggested_skill_pack_id = g.SuggestedSkillPackId,
                        suggested_execution_profile = g.SuggestedExecutionProfile
                    }).ToArray(),
                    remediations = gapAnalysis.Remediations.Select(r => new
                    {
                        kind = r.Kind.ToString(),
                        reason_code = r.ReasonCode,
                        capability = r.Capability,
                        description = r.Description
                    }).ToArray(),
                    applied = remediationResult?.AppliedActions.Select(a => a.Kind.ToString()).ToArray() ?? Array.Empty<string>(),
                    failed = remediationResult?.FailedActions.Select(a => a.Kind.ToString()).ToArray() ?? Array.Empty<string>(),
                    notes = remediationResult?.Notes.ToArray() ?? Array.Empty<string>(),
                    workflow_state = remediationResult?.WorkflowState ?? "none"
                }, JsonDefaults.Web);
            }

            return new AgentMessage
            {
                FromAgentId = effectiveContext.AgentId,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = result.FinalAnswer,
                Payload = responsePayload,
                CorrelationId = effectiveTask.CorrelationId ?? effectiveTask.Id,
                CausationId = effectiveTask.Id
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
                effectiveTask,
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
                Payload = new Dictionary<string, object>(effectiveTask.Payload ?? new Dictionary<string, object>()),
                CorrelationId = effectiveTask.CorrelationId ?? effectiveTask.Id,
                CausationId = effectiveTask.Id
            };
        }
    }

    private static AgentMessage CloneTaskWithPayload(AgentMessage source, Dictionary<string, object> payload)
    {
        return new AgentMessage
        {
            Id = source.Id,
            FromAgentId = source.FromAgentId,
            ToAgentId = source.ToAgentId,
            Type = source.Type,
            Content = source.Content,
            Payload = payload,
            Timestamp = source.Timestamp,
            CorrelationId = source.CorrelationId,
            CausationId = source.CausationId
        };
    }

    private static bool TryResolveSwitchedExecutionProfile(
        CapabilityRemediationExecutionResult remediation,
        out string profile,
        out string reasonCode)
    {
        var switchAction = remediation.AppliedActions
            .LastOrDefault(a => a.Kind == CapabilityRemediationActionKind.SwitchExecutionProfile
                                && !string.IsNullOrWhiteSpace(a.TargetExecutionProfile));

        if (switchAction is null || string.IsNullOrWhiteSpace(switchAction.TargetExecutionProfile))
        {
            profile = string.Empty;
            reasonCode = string.Empty;
            return false;
        }

        profile = switchAction.TargetExecutionProfile.Trim();
        reasonCode = switchAction.ReasonCode;
        return true;
    }

    private static bool IsReplayAlreadyAttempted(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue("capability_gap_replay_attempted", out var raw) || raw is null)
        {
            return false;
        }

        return raw switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => false
        };
    }

    private static string ResolveReplayTaskContent(AgentMessage task, Dictionary<string, object> payload)
    {
        var state = payload.TryGetValue("capability_gap_state", out var stateObj)
            ? stateObj?.ToString()
            : null;

        if (string.Equals(state, "capability_gap_replay_in_progress", StringComparison.OrdinalIgnoreCase)
            && payload.TryGetValue("original_intent", out var originalIntentObj)
            && !string.IsNullOrWhiteSpace(originalIntentObj?.ToString()))
        {
            return originalIntentObj?.ToString() ?? string.Empty;
        }

        return task.Content ?? string.Empty;
    }

    private static void TryAppendExecutionProfileSwitchEvent(
        ReActTaskProcessorContext context,
        AgentMessage task,
        string profile,
        string reasonCode)
    {
        if (context.EventSink is null)
        {
            return;
        }

        try
        {
            context.EventSink.AppendEvent(new AgentEvent
            {
                AgentId = context.AgentId,
                Type = EventType.DecisionMade,
                Description = "Capability remediation execution profile switch applied",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "capability.remediation",
                    ["task_id"] = task.Id,
                    ["reason_code"] = reasonCode,
                    ["action_kind"] = CapabilityRemediationActionKind.SwitchExecutionProfile.ToString(),
                    ["status"] = "applied",
                    ["target_execution_profile"] = profile,
                    ["note"] = $"Execution profile switched to {profile}"
                }
            });
        }
        catch
        {
            // best-effort eventing only
        }
    }

    private static void TryAppendSensitiveInputGuardEvent(
        ReActTaskProcessorContext context,
        AgentMessage task,
        string reasonCode)
    {
        if (context.EventSink is null)
        {
            return;
        }

        try
        {
            context.EventSink.AppendEvent(new AgentEvent
            {
                AgentId = context.AgentId,
                Type = EventType.DecisionMade,
                Description = "Sensitive input guard blocked raw credential payload",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "capability.security",
                    ["task_id"] = task.Id,
                    ["reason_code"] = reasonCode,
                    ["status"] = "blocked"
                }
            });
        }
        catch
        {
            // best-effort eventing only
        }
    }

    private static Persona AppendToolsToPersona(Persona persona, IReadOnlyList<string> additionalTools)
    {
        var mergedTools = persona.AvailableTools
            .Concat(additionalTools)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (mergedTools.SequenceEqual(persona.AvailableTools, StringComparer.OrdinalIgnoreCase))
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
            AvailableTools = mergedTools,
            CustomInstructions = new Dictionary<string, string>(persona.CustomInstructions, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static void EmitCapabilityGapDecisionEvent(
        ReActTaskProcessorContext context,
        AgentMessage task,
        CapabilityGapAnalysisResult analysis,
        CapabilityRemediationExecutionResult? remediation,
        double? remediationDurationMs,
        string workflowState,
        string? terminalReasonCode)
    {
        if (context.EventSink is null)
        {
            return;
        }

        try
        {
            context.EventSink.AppendEvent(new AgentEvent
            {
                AgentId = context.AgentId,
                Type = EventType.DecisionMade,
                Description = "Capability gap analysis completed",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "capability.gap_analysis",
                    ["task_id"] = task.Id,
                    ["gap_workflow_id"] = task.CorrelationId ?? task.Id,
                    ["gap_count"] = analysis.Gaps.Count,
                    ["remediation_count"] = analysis.Remediations.Count,
                    ["failed_remediation_count"] = remediation?.FailedActions.Count ?? 0,
                    ["reason_codes"] = string.Join(",", analysis.Gaps.Select(g => g.ReasonCode).Distinct(StringComparer.OrdinalIgnoreCase)),
                    ["remediation_notes"] = remediation is null ? string.Empty : string.Join(" | ", remediation.Notes),
                    ["workflow_state"] = workflowState,
                    ["terminal_reason_code"] = terminalReasonCode ?? string.Empty,
                    ["remediation_attempted"] = remediation is not null,
                    ["autofix_success"] = string.Equals(workflowState, "capability_gap_resolved_retrying_original_intent", StringComparison.OrdinalIgnoreCase),
                    ["remediation_duration_ms"] = remediationDurationMs ?? 0d
                }
            });
        }
        catch
        {
            // best-effort eventing only
        }
    }

    private static Persona BuildEffectivePersonaForTask(Persona persona, AgentMessage task, AgentSkillRuntimeOverlay? overlay)
    {
        var isHttp = task.Payload is not null
            && task.Payload.TryGetValue("transport", out var transportObj)
            && transportObj is not null
            && string.Equals(transportObj.ToString(), "http", StringComparison.OrdinalIgnoreCase);

        var tools = persona.AvailableTools.AsEnumerable();
        if (overlay is not null && overlay.AdditionalTools.Count > 0)
        {
            tools = tools.Concat(overlay.AdditionalTools);
        }

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

        var filtered = tools
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var specializations = persona.Specializations;
        if (overlay is not null && overlay.AdditionalSpecializations.Count > 0)
        {
            specializations = specializations
                .Concat(overlay.AdditionalSpecializations)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var customInstructions = new Dictionary<string, string>(persona.CustomInstructions, StringComparer.OrdinalIgnoreCase);
        var mergedSystemPrompt = persona.SystemPrompt;
        if (overlay is not null && overlay.PromptFragments.Count > 0)
        {
            mergedSystemPrompt = $"{mergedSystemPrompt}{Environment.NewLine}{Environment.NewLine}# Temporary Skill Guidance{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", overlay.PromptFragments)}";
        }

        if (overlay is not null && overlay.ActiveSkillPackIds.Count > 0)
        {
            customInstructions["runtime_skill_packs"] = string.Join(",", overlay.ActiveSkillPackIds);
        }

        if (filtered.SequenceEqual(persona.AvailableTools, StringComparer.OrdinalIgnoreCase)
            && ReferenceEquals(specializations, persona.Specializations)
            && string.Equals(mergedSystemPrompt, persona.SystemPrompt, StringComparison.Ordinal)
            && customInstructions.Count == persona.CustomInstructions.Count)
        {
            return persona;
        }

        return new Persona
        {
            Name = persona.Name,
            DemonTitle = persona.DemonTitle,
            SystemPrompt = mergedSystemPrompt,
            ModelOverride = persona.ModelOverride,
            Personality = persona.Personality,
            Specializations = specializations,
            AvailableTools = filtered,
            CustomInstructions = customInstructions
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

    private static string AppendRuntimeConstraints(
        string systemContext,
        Persona persona,
        AgentMessage task,
        ReActComplexityAssessment planning)
    {
        var allowed = persona.AvailableTools.Count == 0
            ? "(none)"
            : string.Join(", ", persona.AvailableTools);

        var hasTelegram = TryGetTelegramChatId(task.Payload, out var chatId) && chatId != 0;
        var transport = task.Payload?.TryGetValue("transport", out var t) == true ? t?.ToString() : null;
        var executionProfile = ResolveExecutionProfile(task.Payload);

        var agentCountEmailRule = BuildAgentCountEmailRule(task, persona);

        return $"""
            {systemContext}

            # Runtime Constraints (STRICT)
            - Allowed tools for this task: {allowed}
            - Action MUST be FINAL_ANSWER or one of the allowed tools above.
            - Do NOT call send_telegram unless a real telegram_chat_id is present in the task payload.
            {agentCountEmailRule}
            - execution_profile={executionProfile}
                - task_complexity={planning.Complexity} iteration_budget={planning.IterationBudget} recommended_parallel_branches={planning.RecommendedParallelBranches}
            - transport={transport ?? "(unknown)"} telegram_chat_id={(hasTelegram ? chatId.ToString() : "(none)")}
            """;
    }

    private static string ResolveExecutionProfile(Dictionary<string, object>? payload)
    {
        if (payload is not null)
        {
            if (payload.TryGetValue("execution_profile", out var ep) && !string.IsNullOrWhiteSpace(ep?.ToString()))
            {
                return ep!.ToString()!.Trim();
            }

            if (payload.TryGetValue("profile", out var profile) && !string.IsNullOrWhiteSpace(profile?.ToString()))
            {
                return profile!.ToString()!.Trim();
            }
        }

        return "Research";
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

    private async Task<ReActResult> RunLoopAsync(ReActTaskProcessorContext context, string systemContext, string task, AgentMessage sourceTask, CancellationToken ct)
    {
        async Task EmitCheckpointAsync(ReActCheckpoint checkpoint, CancellationToken token)
        {
            await PersistCheckpointAsync(context, sourceTask, checkpoint, token).ConfigureAwait(false);
        }

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
            PromptBuilder: context.PromptBuilder,
            EmitCheckpoint: EmitCheckpointAsync,
            ExecutionProfile: ResolveExecutionProfile(sourceTask.Payload));

        var result = await context.LoopRunner.RunAsync(loopContext, ct).ConfigureAwait(false);

        return new ReActResult(
            FinalAnswer: result.FinalAnswer,
            Reasoning: result.Reasoning,
            Iterations: result.Iterations,
            ToolCalls: result.ToolCalls.ToList());
    }

    private async Task<ReplayAttemptOutcome> TryRunLoopWithReplayBudgetAsync(
        ReActTaskProcessorContext context,
        string systemContext,
        string task,
        AgentMessage sourceTask,
        CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, context.ReActOptions.ReplayMaxAttempts);
        var timeoutMs = Math.Max(1000, context.ReActOptions.ReplayAttemptTimeoutMs);
        var backoffMs = Math.Max(0, context.ReActOptions.ReplayBackoffMs);

        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            try
            {
                var result = await RunLoopAsync(context, systemContext, task, sourceTask, timeoutCts.Token).ConfigureAwait(false);
                return new ReplayAttemptOutcome(true, result, null, attempt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                lastError = new TimeoutException($"Replay attempt {attempt}/{maxAttempts} timed out after {timeoutMs}ms.", ex);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (attempt < maxAttempts && backoffMs > 0)
            {
                await Task.Delay(backoffMs, ct).ConfigureAwait(false);
            }
        }

        var error = $"Replay failed after {maxAttempts} attempt(s). Last error: {lastError?.Message ?? "unknown"}";
        return new ReplayAttemptOutcome(false, null, error, maxAttempts);
    }

    private static void TryAppendReplayOutcomeEvent(
        ReActTaskProcessorContext context,
        AgentMessage task,
        string status,
        int attempts,
        string? note)
    {
        if (context.EventSink is null)
        {
            return;
        }

        try
        {
            context.EventSink.AppendEvent(new AgentEvent
            {
                AgentId = context.AgentId,
                Type = EventType.DecisionMade,
                Description = "Capability replay outcome",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "capability.replay",
                    ["task_id"] = task.Id,
                    ["gap_workflow_id"] = task.CorrelationId ?? task.Id,
                    ["status"] = status,
                    ["attempts"] = attempts,
                    ["note"] = note ?? string.Empty
                }
            });
        }
        catch
        {
            // best-effort eventing only
        }
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
                Content = response,
                CorrelationId = task.CorrelationId ?? task.Id,
                CausationId = task.Id
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
                Content = $"❌ Error handling command: {ex.Message}",
                CorrelationId = task.CorrelationId ?? task.Id,
                CausationId = task.Id
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

            var result = await RunLoopAsync(context, systemContext, task, message, ct).ConfigureAwait(false);

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
                Content = $"Collaboration response submitted: {preview}...",
                CorrelationId = message.CorrelationId ?? requestId,
                CausationId = message.Id
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

    private static string? GetCollaborationId(AgentMessage sourceTask)
    {
        if (sourceTask.Payload is null)
        {
            return null;
        }

        if (sourceTask.Payload.TryGetValue("collaboration_id", out var collabLower))
        {
            return collabLower?.ToString();
        }

        if (sourceTask.Payload.TryGetValue("CollaborationId", out var collabPascal))
        {
            return collabPascal?.ToString();
        }

        return null;
    }

    private static async Task PersistCheckpointAsync(
        ReActTaskProcessorContext context,
        AgentMessage sourceTask,
        ReActCheckpoint checkpoint,
        CancellationToken ct)
    {
        var collaborationId = GetCollaborationId(sourceTask);
        var payload = JsonSerializer.Serialize(new
        {
            checkpoint_type = "react",
            phase = checkpoint.Phase,
            label = checkpoint.Label,
            detail = checkpoint.Detail,
            iteration = checkpoint.Iteration,
            occurred_at_utc = checkpoint.OccurredAtUtc,
            branch_id = sourceTask.Id,
            task_from_agent_id = sourceTask.FromAgentId,
            task_to_agent_id = sourceTask.ToAgentId,
            collaboration_id = collaborationId
        }, JsonDefaults.Web);

        await context.SharedMemory.AddFactAsync(new Fact
        {
            CreatedBy = context.AgentId,
            Category = "react.checkpoint",
            Content = payload,
            Source = "react_loop",
            Confidence = 1.0,
            Visibility = MemoryVisibility.Public
        }, ct).ConfigureAwait(false);

        try
        {
            context.EventSink?.AppendEvent(new AgentEvent
            {
                AgentId = context.AgentId,
                Type = EventType.DecisionMade,
                Description = checkpoint.Label,
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "react.checkpoint",
                    ["task_id"] = sourceTask.Id,
                    ["phase"] = checkpoint.Phase,
                    ["iteration"] = checkpoint.Iteration,
                    ["content"] = payload
                }
            });
        }
        catch
        {
            // best-effort eventing only
        }
    }

    private sealed record ReActResult(string FinalAnswer, string Reasoning, int Iterations, List<string> ToolCalls);

    private sealed record ReplayAttemptOutcome(bool Success, ReActResult? Result, string? ErrorMessage, int AttemptsUsed);
}
