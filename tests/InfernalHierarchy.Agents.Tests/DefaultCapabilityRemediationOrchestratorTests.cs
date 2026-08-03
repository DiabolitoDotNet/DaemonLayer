using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class DefaultCapabilityRemediationOrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenMaxAttemptsReached_ShouldStopWithBudgetExhausted()
    {
        var orchestrator = new DefaultCapabilityRemediationOrchestrator();
        var context = BuildContext(Mock.Of<IToolRegistry>());

        var analysis = new CapabilityGapAnalysisResult(
            Gaps:
            [
                new CapabilityGap(
                    Capability: "mailbox_read",
                    ReasonCode: "missing_mailbox_read_tool",
                    Description: "Need inbox read tool",
                    BlockedByProfile: false,
                    SuggestedSkillPackId: null,
                    SuggestedExecutionProfile: "Research")
            ],
            Remediations:
            [
                new CapabilityRemediationAction(
                    Kind: CapabilityRemediationActionKind.SwitchExecutionProfile,
                    ReasonCode: "profile_blocked",
                    Capability: "mailbox_read",
                    Description: "Switch profile",
                    TargetExecutionProfile: "Build"),
                new CapabilityRemediationAction(
                    Kind: CapabilityRemediationActionKind.SwitchExecutionProfile,
                    ReasonCode: "profile_blocked",
                    Capability: "mailbox_read",
                    Description: "Switch profile again",
                    TargetExecutionProfile: "Build")
            ],
            Report: new CapabilityGapReport(
                RequestedOutcome: "read inbox",
                MissingCapabilities: new[] { "mailbox_read" },
                CandidateTools: new[] { "email_inbox_query" },
                SecurityRiskClass: CapabilitySecurityRiskClass.Medium,
                CanAutofix: true,
                BlockReasonCode: "missing_mailbox_read_tool"),
            Plan: new CapabilityRemediationPlan(
                PlanId: "plan-budget",
                Steps: Array.Empty<CapabilityRemediationPlanStep>(),
                MaxAttempts: 1,
                MaxDurationSeconds: 120,
                PolicyGateAllowsAutofix: true));

        var result = await orchestrator.ExecuteAsync(
            context,
            BuildTask(),
            analysis,
            CancellationToken.None);

        result.WorkflowState.Should().Be("capability_gap_unresolved_terminal");
        result.TerminalReasonCode.Should().Be("budget_exhausted");
        result.AppliedActions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCollaborationArtifactsMissing_ShouldFailTerminally()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.ExecuteToolWithTrackingAsync(
                "request_collaboration",
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Success = true,
                Output = "Generated artifacts: research.md"
            });

        var orchestrator = new DefaultCapabilityRemediationOrchestrator();
        var context = BuildContext(toolRegistry.Object);

        var analysis = new CapabilityGapAnalysisResult(
            Gaps:
            [
                new CapabilityGap(
                    Capability: "autonomy_audit",
                    ReasonCode: "need_external_review",
                    Description: "Need collaboration audit",
                    BlockedByProfile: false,
                    SuggestedSkillPackId: null,
                    SuggestedExecutionProfile: null)
            ],
            Remediations:
            [
                new CapabilityRemediationAction(
                    Kind: CapabilityRemediationActionKind.EscalateCollaboration,
                    ReasonCode: "request_collaboration_audit",
                    Capability: "autonomy_audit",
                    Description: "Run collaboration audit")
            ],
            Report: new CapabilityGapReport(
                RequestedOutcome: "autonomy closure",
                MissingCapabilities: new[] { "autonomy_audit" },
                CandidateTools: new[] { "request_collaboration" },
                SecurityRiskClass: CapabilitySecurityRiskClass.Medium,
                CanAutofix: true,
                BlockReasonCode: "need_external_review"),
            Plan: new CapabilityRemediationPlan(
                PlanId: "plan-collab",
                Steps: Array.Empty<CapabilityRemediationPlanStep>(),
                MaxAttempts: 3,
                MaxDurationSeconds: 120,
                PolicyGateAllowsAutofix: true));

        var result = await orchestrator.ExecuteAsync(
            context,
            BuildTask(),
            analysis,
            CancellationToken.None);

        result.WorkflowState.Should().Be("capability_gap_unresolved_terminal");
        result.TerminalReasonCode.Should().Be("remediation_action_failed");
        result.FailedActions.Should().HaveCount(1);
        result.Notes.Should().Contain(note =>
            note.Contains("manifest parsing failed", StringComparison.OrdinalIgnoreCase)
            || note.Contains("missing required artifacts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenRemediationSucceeds_ShouldRequestReplay()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.ExecuteToolWithTrackingAsync(
                "create_custom_tool",
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Success = true,
                Output = "tool created"
            });

        var orchestrator = new DefaultCapabilityRemediationOrchestrator();
        var context = BuildContext(toolRegistry.Object);

        var analysis = new CapabilityGapAnalysisResult(
            Gaps:
            [
                new CapabilityGap(
                    Capability: "mailbox_read",
                    ReasonCode: "missing_mailbox_read_tool",
                    Description: "Need inbox read tool",
                    BlockedByProfile: false,
                    SuggestedSkillPackId: null,
                    SuggestedExecutionProfile: "Research")
            ],
            Remediations:
            [
                new CapabilityRemediationAction(
                    Kind: CapabilityRemediationActionKind.CreateCustomTool,
                    ReasonCode: "synthesize_custom_tool",
                    Capability: "mailbox_read",
                    Description: "Create inbox adapter",
                    CustomToolName: "email_inbox_query",
                    CustomToolRequirement: "read-only")
            ],
            Report: new CapabilityGapReport(
                RequestedOutcome: "read inbox",
                MissingCapabilities: new[] { "mailbox_read" },
                CandidateTools: new[] { "email_inbox_query" },
                SecurityRiskClass: CapabilitySecurityRiskClass.Medium,
                CanAutofix: true,
                BlockReasonCode: "missing_mailbox_read_tool"),
            Plan: new CapabilityRemediationPlan(
                PlanId: "plan-ok",
                Steps: Array.Empty<CapabilityRemediationPlanStep>(),
                MaxAttempts: 3,
                MaxDurationSeconds: 120,
                PolicyGateAllowsAutofix: true));

        var result = await orchestrator.ExecuteAsync(
            context,
            BuildTask(),
            analysis,
            CancellationToken.None);

        result.WorkflowState.Should().Be("capability_gap_resolved_retrying_original_intent");
        result.TerminalReasonCode.Should().Be("none");
        result.ReplayRequested.Should().BeTrue();
        result.NewlyAvailableTools.Should().Contain("email_inbox_query");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCollaborationReturnsManifest_ShouldAcceptArtifacts()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.ExecuteToolWithTrackingAsync(
                "request_collaboration",
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                Success = true,
                Output = """
                    {
                      "artifacts": [
                        { "path": "research.md", "exists": true, "status": "pass" },
                        { "path": "design.json", "exists": true, "status": "pass" },
                        { "path": "test-report.json", "exists": true, "status": "pass" },
                        { "path": "security-report.json", "exists": true, "status": "pass" }
                      ],
                      "allChecksPassed": true
                    }
                    """
            });

        var orchestrator = new DefaultCapabilityRemediationOrchestrator();
        var context = BuildContext(toolRegistry.Object);

        var analysis = new CapabilityGapAnalysisResult(
            Gaps:
            [
                new CapabilityGap(
                    Capability: "integration_qualification",
                    ReasonCode: "requires_capability_qualification",
                    Description: "Need qualification",
                    BlockedByProfile: false,
                    SuggestedSkillPackId: null,
                    SuggestedExecutionProfile: "Research")
            ],
            Remediations:
            [
                new CapabilityRemediationAction(
                    Kind: CapabilityRemediationActionKind.EscalateCollaboration,
                    ReasonCode: "capability_qualification_required",
                    Capability: "integration_qualification",
                    Description: "Run qualification workflow")
            ],
            Report: new CapabilityGapReport(
                RequestedOutcome: "integration",
                MissingCapabilities: new[] { "integration_qualification" },
                CandidateTools: new[] { "request_collaboration" },
                SecurityRiskClass: CapabilitySecurityRiskClass.Medium,
                CanAutofix: true,
                BlockReasonCode: "requires_capability_qualification"),
            Plan: new CapabilityRemediationPlan(
                PlanId: "plan-manifest",
                Steps: Array.Empty<CapabilityRemediationPlanStep>(),
                MaxAttempts: 3,
                MaxDurationSeconds: 120,
                PolicyGateAllowsAutofix: true));

        var result = await orchestrator.ExecuteAsync(
            context,
            BuildTask(),
            analysis,
            CancellationToken.None);

        result.WorkflowState.Should().Be("capability_gap_resolved_retrying_original_intent");
        result.FailedActions.Should().BeEmpty();
        result.ReplayRequested.Should().BeTrue();
    }

    private static AgentMessage BuildTask()
    {
        return new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            FromAgentId = "user",
            ToAgentId = "lucifer",
            Type = MessageType.Task,
            Content = "close capability gaps"
        };
    }

    private static ReActTaskProcessorContext BuildContext(IToolRegistry toolRegistry)
    {
        var loopRunner = new Mock<IReActLoopRunner>();
        loopRunner.Setup(x => x.RunAsync(It.IsAny<ReActLoopContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReActLoopResult("done", "ok", 1, Array.Empty<string>()));

        return new ReActTaskProcessorContext(
            AgentId: "lucifer",
            AgentName: "Lucifer",
            AgentRank: AgentRank.Supreme,
            Persona: new Persona
            {
                Name = "Lucifer",
                SystemPrompt = "You are Lucifer",
                AvailableTools = new[] { "create_custom_tool", "request_collaboration", "request_skill_pack" }
            },
            LlmClient: Mock.Of<ILlmClient>(),
            ToolRegistry: toolRegistry,
            SharedMemory: Mock.Of<ISharedMemory>(),
            ActionParser: Mock.Of<IActionParser>(),
            ActionExecutor: Mock.Of<IActionExecutor>(),
            ReportGenerator: Mock.Of<IReportGenerator>(),
            PromptBuilder: Mock.Of<IReActPromptBuilder>(),
            LoopRunner: loopRunner.Object,
            ReActOptions: new ReActOptions { UseJsonResponse = true },
            RagOptions: new RagOptions { Enabled = false },
            VectorMemory: null,
            CollaborationService: null,
            RuntimeSkillStore: null,
            EventSink: null,
            SetStatus: _ => { },
            BuildBaseContextAsync: (_, _) => Task.FromResult("base"),
            Logger: Mock.Of<ILogger>());
    }
}
