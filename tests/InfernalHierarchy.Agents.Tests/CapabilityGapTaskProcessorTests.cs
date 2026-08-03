using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class CapabilityGapTaskProcessorTests
{
    [Fact]
    public async Task ProcessAsync_WhenSensitiveCredentialsProvidedWithoutSecretReference_ShouldBlockEarly()
    {
        var processor = new DefaultReActTaskProcessor(
            new DefaultRagContextEnricher(),
            new DefaultAgentEventAppender());

        var context = BuildContext();

        var response = await processor.ProcessAsync(context, new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            FromAgentId = "user",
            ToAgentId = "lucifer",
            Type = MessageType.Task,
            Content = "Access my mailbox with login foo@example.com and password Hunter2"
        }, CancellationToken.None);

        response.Content.Should().Contain("Sensitive credentials detected");
        response.Payload.Should().ContainKey("capability_gap_state");
        response.Payload["capability_gap_state"].Should().Be("blocked_by_sensitive_input_guard");
    }

    [Fact]
    public async Task ProcessAsync_WhenGapRemediationApplied_ShouldExposeRetryWorkflowState()
    {
        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var analyzer = new Mock<ICapabilityGapAnalyzer>();
        analyzer.Setup(x => x.AnalyzeAsync(
                It.IsAny<ReActTaskProcessorContext>(),
                It.IsAny<AgentMessage>(),
                It.IsAny<Persona>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityGapAnalysisResult(
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
                    RequestedOutcome: "Check inbox",
                    MissingCapabilities: new[] { "mailbox_read" },
                    CandidateTools: new[] { "email_inbox_query" },
                    SecurityRiskClass: CapabilitySecurityRiskClass.Medium,
                    CanAutofix: true,
                    BlockReasonCode: "missing_mailbox_read_tool"),
                Plan: new CapabilityRemediationPlan(
                    PlanId: "plan-1",
                    Steps: new[]
                    {
                        new CapabilityRemediationPlanStep(
                            Name: "retry_original_task",
                            Description: "retry",
                            IsAutomated: true,
                            ActionKind: "RetryOriginalIntent",
                            Capability: "original_intent")
                    },
                    MaxAttempts: 3,
                    MaxDurationSeconds: 120,
                    PolicyGateAllowsAutofix: true)));

        var remediator = new Mock<ICapabilityRemediationOrchestrator>();
        remediator.Setup(x => x.ExecuteAsync(
                It.IsAny<ReActTaskProcessorContext>(),
                It.IsAny<AgentMessage>(),
                It.IsAny<CapabilityGapAnalysisResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityRemediationExecutionResult(
                AppliedActions:
                [
                    new CapabilityRemediationAction(
                        Kind: CapabilityRemediationActionKind.CreateCustomTool,
                        ReasonCode: "synthesize_custom_tool",
                        Capability: "mailbox_read",
                        Description: "Create inbox adapter",
                        CustomToolName: "email_inbox_query",
                        CustomToolRequirement: "read-only")
                ],
                FailedActions: Array.Empty<CapabilityRemediationAction>(),
                NewlyAvailableTools: new[] { "email_inbox_query" },
                Notes: new[] { "created" },
                WorkflowState: "capability_gap_resolved_retrying_original_intent",
                TerminalReasonCode: "none",
                ReplayRequested: true));

        var processor = new DefaultReActTaskProcessor(
            new DefaultRagContextEnricher(),
            new DefaultAgentEventAppender(),
            analyzer.Object,
            remediator.Object);

        var context = BuildContext(sharedMemory: sharedMemory.Object);

        var response = await processor.ProcessAsync(context, new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            FromAgentId = "user",
            ToAgentId = "lucifer",
            Type = MessageType.Task,
            Content = "Check inbox for alerts"
        }, CancellationToken.None);

        response.Payload.Should().ContainKey("capability_gap_state");
        response.Payload["capability_gap_state"].Should().Be("capability_gap_replay_in_progress");
        response.Payload.Should().ContainKey("capability_gap_replay_attempted");
        response.Payload["capability_gap_replay_attempted"].Should().Be(true);
    }

    [Fact]
    public async Task ProcessAsync_WhenReplayAlreadyAttempted_ShouldReturnReplayGuardTerminalMessage()
    {
        var analyzer = new Mock<ICapabilityGapAnalyzer>();
        analyzer.Setup(x => x.AnalyzeAsync(
                It.IsAny<ReActTaskProcessorContext>(),
                It.IsAny<AgentMessage>(),
                It.IsAny<Persona>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityGapAnalysisResult(
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
                    RequestedOutcome: "Check inbox",
                    MissingCapabilities: new[] { "mailbox_read" },
                    CandidateTools: new[] { "email_inbox_query" },
                    SecurityRiskClass: CapabilitySecurityRiskClass.Medium,
                    CanAutofix: true,
                    BlockReasonCode: "missing_mailbox_read_tool"),
                Plan: new CapabilityRemediationPlan(
                    PlanId: "plan-guard",
                    Steps: new[]
                    {
                        new CapabilityRemediationPlanStep(
                            Name: "retry_original_task",
                            Description: "retry",
                            IsAutomated: true,
                            ActionKind: "RetryOriginalIntent",
                            Capability: "original_intent")
                    },
                    MaxAttempts: 3,
                    MaxDurationSeconds: 120,
                    PolicyGateAllowsAutofix: true)));

        var remediator = new Mock<ICapabilityRemediationOrchestrator>();
        remediator.Setup(x => x.ExecuteAsync(
                It.IsAny<ReActTaskProcessorContext>(),
                It.IsAny<AgentMessage>(),
                It.IsAny<CapabilityGapAnalysisResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityRemediationExecutionResult(
                AppliedActions:
                [
                    new CapabilityRemediationAction(
                        Kind: CapabilityRemediationActionKind.CreateCustomTool,
                        ReasonCode: "synthesize_custom_tool",
                        Capability: "mailbox_read",
                        Description: "Create inbox adapter",
                        CustomToolName: "email_inbox_query",
                        CustomToolRequirement: "read-only")
                ],
                FailedActions: Array.Empty<CapabilityRemediationAction>(),
                NewlyAvailableTools: new[] { "email_inbox_query" },
                Notes: new[] { "created" },
                WorkflowState: "capability_gap_resolved_retrying_original_intent",
                TerminalReasonCode: "none",
                ReplayRequested: true));

        var loopRunner = new Mock<IReActLoopRunner>();
        loopRunner.Setup(x => x.RunAsync(It.IsAny<ReActLoopContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReActLoopResult("done", "ok", 1, Array.Empty<string>()));

        var processor = new DefaultReActTaskProcessor(
            new DefaultRagContextEnricher(),
            new DefaultAgentEventAppender(),
            analyzer.Object,
            remediator.Object);

        var context = BuildContext(loopRunner: loopRunner.Object);

        var response = await processor.ProcessAsync(context, new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            FromAgentId = "user",
            ToAgentId = "lucifer",
            Type = MessageType.Task,
            Content = "Check inbox for alerts",
            Payload = new Dictionary<string, object>
            {
                ["capability_gap_replay_attempted"] = true
            }
        }, CancellationToken.None);

        response.Content.Should().Contain("replay guard prevented duplicate automatic replay");
        response.Payload["capability_gap_state"].Should().Be("capability_gap_replay_guard_triggered");
        loopRunner.Verify(x => x.RunAsync(It.IsAny<ReActLoopContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenReplayFirstAttemptFails_ShouldRetryAndSucceedWithinBudget()
    {
        var analyzer = new Mock<ICapabilityGapAnalyzer>();
        analyzer.Setup(x => x.AnalyzeAsync(
                It.IsAny<ReActTaskProcessorContext>(),
                It.IsAny<AgentMessage>(),
                It.IsAny<Persona>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityGapAnalysisResult(
                Gaps: [new CapabilityGap("mailbox_read", "missing_mailbox_read_tool", "Need inbox read tool", false, null, "Research")],
                Remediations: [new CapabilityRemediationAction(CapabilityRemediationActionKind.CreateCustomTool, "synthesize_custom_tool", "mailbox_read", "Create inbox adapter", CustomToolName: "email_inbox_query", CustomToolRequirement: "read-only")],
                Report: new CapabilityGapReport("Check inbox", new[] { "mailbox_read" }, new[] { "email_inbox_query" }, CapabilitySecurityRiskClass.Medium, true, "missing_mailbox_read_tool"),
                Plan: new CapabilityRemediationPlan("plan-replay", Array.Empty<CapabilityRemediationPlanStep>(), 3, 120, true)));

        var remediator = new Mock<ICapabilityRemediationOrchestrator>();
        remediator.Setup(x => x.ExecuteAsync(
                It.IsAny<ReActTaskProcessorContext>(),
                It.IsAny<AgentMessage>(),
                It.IsAny<CapabilityGapAnalysisResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityRemediationExecutionResult(
                AppliedActions: [new CapabilityRemediationAction(CapabilityRemediationActionKind.CreateCustomTool, "synthesize_custom_tool", "mailbox_read", "Create inbox adapter", CustomToolName: "email_inbox_query", CustomToolRequirement: "read-only")],
                FailedActions: Array.Empty<CapabilityRemediationAction>(),
                NewlyAvailableTools: new[] { "email_inbox_query" },
                Notes: new[] { "created" },
                WorkflowState: "capability_gap_resolved_retrying_original_intent",
                TerminalReasonCode: "none",
                ReplayRequested: true));

        var loopRunner = new Mock<IReActLoopRunner>();
        loopRunner.SetupSequence(x => x.RunAsync(It.IsAny<ReActLoopContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient failure"))
            .ReturnsAsync(new ReActLoopResult("done", "ok", 1, Array.Empty<string>()));

        var processor = new DefaultReActTaskProcessor(
            new DefaultRagContextEnricher(),
            new DefaultAgentEventAppender(),
            analyzer.Object,
            remediator.Object);

        var context = BuildContext(
            loopRunner: loopRunner.Object,
            reActOptions: new ReActOptions { UseJsonResponse = true, ReplayMaxAttempts = 2, ReplayAttemptTimeoutMs = 5000, ReplayBackoffMs = 0 });

        var response = await processor.ProcessAsync(context, new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            FromAgentId = "user",
            ToAgentId = "lucifer",
            Type = MessageType.Task,
            Content = "Check inbox for alerts"
        }, CancellationToken.None);

        response.Content.Should().Be("done");
        loopRunner.Verify(x => x.RunAsync(It.IsAny<ReActLoopContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_WhenReplayBudgetExhausted_ShouldReturnTerminalReport()
    {
        var analyzer = new Mock<ICapabilityGapAnalyzer>();
        analyzer.Setup(x => x.AnalyzeAsync(
                It.IsAny<ReActTaskProcessorContext>(),
                It.IsAny<AgentMessage>(),
                It.IsAny<Persona>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityGapAnalysisResult(
                Gaps: [new CapabilityGap("mailbox_read", "missing_mailbox_read_tool", "Need inbox read tool", false, null, "Research")],
                Remediations: [new CapabilityRemediationAction(CapabilityRemediationActionKind.CreateCustomTool, "synthesize_custom_tool", "mailbox_read", "Create inbox adapter", CustomToolName: "email_inbox_query", CustomToolRequirement: "read-only")],
                Report: new CapabilityGapReport("Check inbox", new[] { "mailbox_read" }, new[] { "email_inbox_query" }, CapabilitySecurityRiskClass.Medium, true, "missing_mailbox_read_tool"),
                Plan: new CapabilityRemediationPlan("plan-replay-fail", Array.Empty<CapabilityRemediationPlanStep>(), 3, 120, true)));

        var remediator = new Mock<ICapabilityRemediationOrchestrator>();
        remediator.Setup(x => x.ExecuteAsync(
                It.IsAny<ReActTaskProcessorContext>(),
                It.IsAny<AgentMessage>(),
                It.IsAny<CapabilityGapAnalysisResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityRemediationExecutionResult(
                AppliedActions: [new CapabilityRemediationAction(CapabilityRemediationActionKind.CreateCustomTool, "synthesize_custom_tool", "mailbox_read", "Create inbox adapter", CustomToolName: "email_inbox_query", CustomToolRequirement: "read-only")],
                FailedActions: Array.Empty<CapabilityRemediationAction>(),
                NewlyAvailableTools: new[] { "email_inbox_query" },
                Notes: new[] { "created" },
                WorkflowState: "capability_gap_resolved_retrying_original_intent",
                TerminalReasonCode: "none",
                ReplayRequested: true));

        var loopRunner = new Mock<IReActLoopRunner>();
        loopRunner.Setup(x => x.RunAsync(It.IsAny<ReActLoopContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("still failing"));

        var processor = new DefaultReActTaskProcessor(
            new DefaultRagContextEnricher(),
            new DefaultAgentEventAppender(),
            analyzer.Object,
            remediator.Object);

        var context = BuildContext(
            loopRunner: loopRunner.Object,
            reActOptions: new ReActOptions { UseJsonResponse = true, ReplayMaxAttempts = 2, ReplayAttemptTimeoutMs = 5000, ReplayBackoffMs = 0 });

        var response = await processor.ProcessAsync(context, new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            FromAgentId = "user",
            ToAgentId = "lucifer",
            Type = MessageType.Task,
            Content = "Check inbox for alerts"
        }, CancellationToken.None);

        response.Content.Should().Contain("replay exhausted retry budget");
        response.Payload["capability_gap_terminal_reason_code"].Should().Be("replay_budget_exhausted");
    }

    private static ReActTaskProcessorContext BuildContext(
        ISharedMemory? sharedMemory = null,
        IReActLoopRunner? loopRunner = null,
        ReActOptions? reActOptions = null)
    {
        var effectiveLoopRunner = loopRunner ?? new Mock<IReActLoopRunner>().Object;

        if (loopRunner is null)
        {
            var defaultLoopRunner = new Mock<IReActLoopRunner>();
            defaultLoopRunner.Setup(x => x.RunAsync(It.IsAny<ReActLoopContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReActLoopResult(
                FinalAnswer: "done",
                Reasoning: "ok",
                Iterations: 1,
                ToolCalls: Array.Empty<string>()));

            effectiveLoopRunner = defaultLoopRunner.Object;
        }

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
            ToolRegistry: Mock.Of<IToolRegistry>(),
            SharedMemory: sharedMemory ?? Mock.Of<ISharedMemory>(),
            ActionParser: Mock.Of<IActionParser>(),
            ActionExecutor: Mock.Of<IActionExecutor>(),
            ReportGenerator: Mock.Of<IReportGenerator>(),
            PromptBuilder: Mock.Of<IReActPromptBuilder>(),
            LoopRunner: effectiveLoopRunner,
            ReActOptions: reActOptions ?? new ReActOptions { UseJsonResponse = true },
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
