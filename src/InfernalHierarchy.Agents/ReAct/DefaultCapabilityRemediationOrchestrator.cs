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
                            continue;
                        }

                        newTools.Add(action.CustomToolName);
                        applied.Add(action);
                        notes.Add($"custom tool created: {action.CustomToolName}");
                        break;

                    case CapabilityRemediationActionKind.RequestSkillPack:
                        if (string.IsNullOrWhiteSpace(action.SkillPackId))
                        {
                            failed.Add(action);
                            notes.Add($"request_skill_pack skipped for {action.Capability}: missing skill pack id");
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
                            continue;
                        }

                        applied.Add(action);
                        notes.Add($"skill pack granted: {action.SkillPackId}");
                        break;

                    case CapabilityRemediationActionKind.SwitchExecutionProfile:
                        applied.Add(action);
                        notes.Add($"execution profile switch recommended: {action.TargetExecutionProfile ?? "Build"}");
                        break;

                    case CapabilityRemediationActionKind.EscalateCollaboration:
                        applied.Add(action);
                        notes.Add($"collaboration escalation recommended for capability {action.Capability}");
                        break;

                    default:
                        failed.Add(action);
                        notes.Add($"unsupported remediation action: {action.Kind}");
                        break;
                }
            }
            catch (Exception ex)
            {
                failed.Add(action);
                notes.Add($"remediation action {action.Kind} failed: {ex.Message}");
            }
        }

        return new CapabilityRemediationExecutionResult(
            AppliedActions: applied,
            FailedActions: failed,
            NewlyAvailableTools: newTools.ToArray(),
            Notes: notes);
    }
}
