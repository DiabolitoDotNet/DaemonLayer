namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultCapabilityRemediationOrchestrator : ICapabilityRemediationOrchestrator
{
    private static readonly string[] RequiredAuditArtifacts =
    [
        "research.md",
        "design.json",
        "test-report.json",
        "security-report.json"
    ];

    public async Task<CapabilityRemediationExecutionResult> ExecuteAsync(
        ReActTaskProcessorContext context,
        AgentMessage task,
        CapabilityGapAnalysisResult analysis,
        CancellationToken ct)
    {
        var expectedActionCount = analysis.Remediations.Count;
        var applied = new List<CapabilityRemediationAction>(expectedActionCount);
        var failed = new List<CapabilityRemediationAction>(expectedActionCount);
        var notes = new List<string>(Math.Max(4, expectedActionCount));
        var newTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workflowState = "capability_gap_detected";
        var terminalReasonCode = "none";

        var maxAttempts = Math.Max(1, analysis.Plan?.MaxAttempts ?? analysis.Remediations.Count);
        var maxDuration = TimeSpan.FromSeconds(Math.Max(1, analysis.Plan?.MaxDurationSeconds ?? 120));
        var startedAt = DateTime.UtcNow;
        var attemptsUsed = 0;
        var collaborationCalls = 0;
        var toolCalls = 0;

        const int maxCollaborationCalls = 2;
        const int maxToolCalls = 8;

        foreach (var action in analysis.Remediations)
        {
            ct.ThrowIfCancellationRequested();

            if (DateTime.UtcNow - startedAt > maxDuration)
            {
                terminalReasonCode = "duration_exhausted";
                notes.Add($"remediation duration budget exhausted ({maxDuration.TotalSeconds:0}s)");
                EmitRemediationDecisionEvent(context, task, action, status: "guardrail_triggered", note: terminalReasonCode);
                break;
            }

            if (attemptsUsed >= maxAttempts)
            {
                terminalReasonCode = "budget_exhausted";
                notes.Add($"remediation attempts exhausted ({attemptsUsed}/{maxAttempts})");
                EmitRemediationDecisionEvent(context, task, action, status: "guardrail_triggered", note: terminalReasonCode);
                break;
            }

            if (toolCalls >= maxToolCalls)
            {
                terminalReasonCode = "tool_call_budget_exhausted";
                notes.Add($"remediation tool call budget exhausted ({toolCalls}/{maxToolCalls})");
                EmitRemediationDecisionEvent(context, task, action, status: "guardrail_triggered", note: terminalReasonCode);
                break;
            }

            attemptsUsed++;

            EmitRemediationDecisionEvent(context, task, action, status: "started");

            try
            {
                switch (action.Kind)
                {
                    case CapabilityRemediationActionKind.CreateCustomTool:
                        if (string.IsNullOrWhiteSpace(action.CustomToolName)
                            || string.IsNullOrWhiteSpace(action.CustomToolRequirement))
                        {
                            failed.Add(action);
                            notes.Add($"create_custom_tool skipped for {action.Capability}: missing synthesis inputs");
                            EmitRemediationDecisionEvent(context, task, action, status: "failed", note: "missing synthesis inputs");
                            continue;
                        }

                        var createResult = await context.ToolRegistry.ExecuteToolWithTrackingAsync(
                            "create_custom_tool",
                            new Dictionary<string, object>
                            {
                                ["tool_name"] = action.CustomToolName,
                                ["requirement"] = action.CustomToolRequirement,
                                ["agent_id"] = context.AgentId,
                                ["agent_name"] = context.AgentName,
                                ["agent_rank"] = context.AgentRank.ToString()
                            },
                            agentId: context.AgentId,
                            agentRank: context.AgentRank.ToString(),
                            agentName: context.AgentName,
                            ct: ct).ConfigureAwait(false);
                        toolCalls++;

                        if (!createResult.Success)
                        {
                            failed.Add(action);
                            notes.Add($"create_custom_tool failed for {action.Capability}: {createResult.Error ?? createResult.Output}");
                            EmitRemediationDecisionEvent(context, task, action, status: "failed", note: createResult.Error ?? createResult.Output);
                            continue;
                        }

                        newTools.Add(action.CustomToolName);
                        applied.Add(action);
                        notes.Add($"custom tool created: {action.CustomToolName}");
                        EmitRemediationDecisionEvent(context, task, action, status: "applied", note: $"created tool {action.CustomToolName}");
                        break;

                    case CapabilityRemediationActionKind.RequestSkillPack:
                        if (string.IsNullOrWhiteSpace(action.SkillPackId))
                        {
                            failed.Add(action);
                            notes.Add($"request_skill_pack skipped for {action.Capability}: missing skill pack id");
                            EmitRemediationDecisionEvent(context, task, action, status: "failed", note: "missing skill pack id");
                            continue;
                        }

                        var requestResult = await context.ToolRegistry.ExecuteToolWithTrackingAsync(
                            "request_skill_pack",
                            new Dictionary<string, object>
                            {
                                ["skill_pack_id"] = action.SkillPackId,
                                ["reason"] = $"Autonomous capability closure for {action.Capability}",
                                ["agent_id"] = context.AgentId,
                                ["agent_rank"] = context.AgentRank.ToString(),
                                ["target_agent_id"] = context.AgentId,
                                ["target_agent_rank"] = context.AgentRank.ToString(),
                                ["temporary"] = true,
                                ["ttl_minutes"] = 120
                            },
                            agentId: context.AgentId,
                            agentRank: context.AgentRank.ToString(),
                            agentName: context.AgentName,
                            ct: ct).ConfigureAwait(false);
                        toolCalls++;

                        if (!requestResult.Success)
                        {
                            failed.Add(action);
                            notes.Add($"request_skill_pack failed for {action.Capability}: {requestResult.Error ?? requestResult.Output}");
                            EmitRemediationDecisionEvent(context, task, action, status: "failed", note: requestResult.Error ?? requestResult.Output);
                            continue;
                        }

                        applied.Add(action);
                        notes.Add($"skill pack granted: {action.SkillPackId}");
                        EmitRemediationDecisionEvent(context, task, action, status: "applied", note: $"granted skill pack {action.SkillPackId}");
                        break;

                    case CapabilityRemediationActionKind.SwitchExecutionProfile:
                        applied.Add(action);
                        notes.Add($"execution profile switch recommended: {action.TargetExecutionProfile ?? "Build"}");
                        EmitRemediationDecisionEvent(context, task, action, status: "recommended", note: $"switch profile to {action.TargetExecutionProfile ?? "Build"}");
                        break;

                    case CapabilityRemediationActionKind.EscalateCollaboration:
                        if (collaborationCalls >= maxCollaborationCalls)
                        {
                            failed.Add(action);
                            terminalReasonCode = "collaboration_budget_exhausted";
                            notes.Add($"collaboration guardrail triggered ({collaborationCalls}/{maxCollaborationCalls})");
                            EmitRemediationDecisionEvent(context, task, action, status: "guardrail_triggered", note: terminalReasonCode);
                            break;
                        }

                        collaborationCalls++;
                        if (await TryRunCollaborationAuditAsync(context, task, action, notes, ct).ConfigureAwait(false))
                        {
                            applied.Add(action);
                            EmitRemediationDecisionEvent(context, task, action, status: "applied", note: "collaboration audit executed");
                        }
                        else
                        {
                            failed.Add(action);
                            EmitRemediationDecisionEvent(context, task, action, status: "failed", note: "collaboration audit failed");
                        }
                        break;

                    default:
                        failed.Add(action);
                        notes.Add($"unsupported remediation action: {action.Kind}");
                        EmitRemediationDecisionEvent(context, task, action, status: "failed", note: "unsupported remediation action");
                        break;
                }
            }
            catch (Exception ex)
            {
                failed.Add(action);
                notes.Add($"remediation action {action.Kind} failed: {ex.Message}");
                EmitRemediationDecisionEvent(context, task, action, status: "failed", note: ex.Message);
            }
        }

        if (analysis.HasGaps)
        {
            if (failed.Count > 0 || !string.Equals(terminalReasonCode, "none", StringComparison.OrdinalIgnoreCase))
            {
                workflowState = "capability_gap_unresolved_terminal";
                if (string.Equals(terminalReasonCode, "none", StringComparison.OrdinalIgnoreCase))
                {
                    terminalReasonCode = "remediation_action_failed";
                }
            }
            else
            {
                workflowState = "capability_gap_resolved_retrying_original_intent";
            }
        }

        return new CapabilityRemediationExecutionResult(
            AppliedActions: applied,
            FailedActions: failed,
            NewlyAvailableTools: newTools.ToArray(),
            Notes: notes,
            WorkflowState: workflowState,
            TerminalReasonCode: terminalReasonCode,
            ReplayRequested: string.Equals(workflowState, "capability_gap_resolved_retrying_original_intent", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> TryRunCollaborationAuditAsync(
        ReActTaskProcessorContext context,
        AgentMessage task,
        CapabilityRemediationAction action,
        List<string> notes,
        CancellationToken ct)
    {
        var parameters = new Dictionary<string, object>
        {
            ["task"] =
                $"Capability-gap audit for '{action.Capability}'. Produce: research.md, design.json, test-report.json, security-report.json. Original intent: {task.Content}",
            ["strategy"] = "weighted",
            ["min_participants"] = 2,
            ["agent_id"] = context.AgentId,
            ["include_thinking"] = true
        };

        var result = await context.ToolRegistry.ExecuteToolWithTrackingAsync(
            "request_collaboration",
            parameters,
            agentId: context.AgentId,
            agentRank: context.AgentRank.ToString(),
            agentName: context.AgentName,
            ct: ct).ConfigureAwait(false);

        if (!result.Success)
        {
            notes.Add($"collaboration audit failed for {action.Capability}: {result.Error ?? result.Output}");
            return false;
        }

        if (!ContainsRequiredArtifacts(result.Output, out var missingArtifacts))
        {
            notes.Add($"collaboration audit missing required artifacts for {action.Capability}: {string.Join(", ", missingArtifacts)}");
            return false;
        }

        notes.Add($"collaboration audit executed for {action.Capability}; expected artifacts: research.md, design.json, test-report.json, security-report.json");
        return true;
    }

    private static bool ContainsRequiredArtifacts(string? output, out List<string> missingArtifacts)
    {
        missingArtifacts = new List<string>(RequiredAuditArtifacts.Length);

        if (string.IsNullOrWhiteSpace(output))
        {
            missingArtifacts.AddRange(RequiredAuditArtifacts);
            return false;
        }

        foreach (var artifact in RequiredAuditArtifacts)
        {
            if (output.IndexOf(artifact, StringComparison.OrdinalIgnoreCase) < 0)
            {
                missingArtifacts.Add(artifact);
            }
        }

        return missingArtifacts.Count == 0;
    }

    private static void EmitRemediationDecisionEvent(
        ReActTaskProcessorContext context,
        AgentMessage task,
        CapabilityRemediationAction action,
        string status,
        string? note = null)
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
                Description = "Capability remediation action",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "capability.remediation",
                    ["task_id"] = task.Id,
                    ["gap_workflow_id"] = task.CorrelationId ?? task.Id,
                    ["reason_code"] = action.ReasonCode,
                    ["capability"] = action.Capability,
                    ["action_kind"] = action.Kind.ToString(),
                    ["status"] = status,
                    ["target_execution_profile"] = action.TargetExecutionProfile ?? string.Empty,
                    ["skill_pack_id"] = action.SkillPackId ?? string.Empty,
                    ["custom_tool_name"] = action.CustomToolName ?? string.Empty,
                    ["note"] = note ?? string.Empty
                }
            });
        }
        catch
        {
            // best-effort eventing only
        }
    }
}
