namespace InfernalHierarchy.Agents.ReAct;

public sealed class DefaultCapabilityRemediationOrchestrator : ICapabilityRemediationOrchestrator
{
    public async Task<CapabilityRemediationExecutionResult> ExecuteAsync(
        ReActTaskProcessorContext context,
        AgentMessage task,
        CapabilityGapAnalysisResult analysis,
        CancellationToken ct)
    {
        var applied = new List<CapabilityRemediationAction>();
        var failed = new List<CapabilityRemediationAction>();
        var notes = new List<string>();
        var newTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in analysis.Remediations)
        {
            ct.ThrowIfCancellationRequested();

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
                        applied.Add(action);
                        notes.Add($"collaboration escalation recommended for capability {action.Capability}");
                        EmitRemediationDecisionEvent(context, task, action, status: "recommended", note: "collaboration escalation");
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

        return new CapabilityRemediationExecutionResult(
            AppliedActions: applied,
            FailedActions: failed,
            NewlyAvailableTools: newTools.ToArray(),
            Notes: notes);
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
