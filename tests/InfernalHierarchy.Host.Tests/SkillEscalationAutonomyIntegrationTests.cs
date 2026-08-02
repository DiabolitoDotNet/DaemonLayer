using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Agents;
using InfernalHierarchy.Tools.Tools.Agent;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class SkillEscalationAutonomyIntegrationTests
{
    [Fact]
    public async Task EscalationRequest_ShouldBeAutoApproved_AndAppliedToTaskRuntimePersona()
    {
        var runtimeStore = new InMemoryAgentSkillRuntimeStore();

        var catalog = new Mock<ISkillPackCatalog>();
        catalog.Setup(x => x.GetByIdAsync("critical-pack", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillPack
            {
                Id = "critical-pack",
                Enabled = true,
                AdditionalTools = new[] { "request_collaboration" },
                AdditionalSpecializations = new[] { "Risk arbitration" },
                PromptFragments = new[] { "Favor deterministic escalation handling." }
            });

        var policy = new Mock<IAgentSkillAssignmentPolicy>();
        policy.Setup(x => x.EvaluateTemporarySkillRequestAsync(It.IsAny<SkillAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillAssignmentDecision.EscalationRequired("high_risk_escalation", "manager approval required"));

        var requestTool = new RequestSkillPackTool(
            Mock.Of<ILogger<RequestSkillPackTool>>(),
            catalog.Object,
            policy.Object,
            runtimeStore,
            options: Microsoft.Extensions.Options.Options.Create(new AgentSkillAssignmentOptions
            {
                AutoApproveEscalationsByMainAgent = true,
                MainAgentId = "lucifer"
            }),
            eventSink: null);

        var toolResult = await requestTool.ExecuteAsync(new Dictionary<string, object>
        {
            ["skill_pack_id"] = "critical-pack",
            ["reason"] = "Need high-risk capability",
            ["agent_id"] = "agent-42",
            ["agent_rank"] = "Duke"
        }, CancellationToken.None);

        toolResult.Success.Should().BeTrue();
        toolResult.Metadata["decision"].Should().Be("approved");
        toolResult.Metadata["reason_code"].Should().Be("auto_approved_by_main_agent");

        var capturedLoop = new CapturingLoopRunner();

        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = new DefaultReActTaskProcessor(
            new DefaultRagContextEnricher(),
            new DefaultAgentEventAppender());

        var basePersona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares.",
            AvailableTools = new[] { "read_memory" },
            Specializations = new[] { "Planning" }
        };

        var context = new ReActTaskProcessorContext(
            AgentId: "agent-42",
            AgentName: "Agares",
            AgentRank: AgentRank.Duke,
            Persona: basePersona,
            LlmClient: Mock.Of<ILlmClient>(),
            ToolRegistry: Mock.Of<IToolRegistry>(),
            SharedMemory: sharedMemory.Object,
            ActionParser: Mock.Of<IActionParser>(),
            ActionExecutor: Mock.Of<IActionExecutor>(),
            ReportGenerator: Mock.Of<IReportGenerator>(),
            PromptBuilder: Mock.Of<IReActPromptBuilder>(),
            LoopRunner: capturedLoop,
            ReActOptions: new ReActOptions { UseJsonResponse = true },
            RagOptions: new RagOptions { Enabled = false },
            VectorMemory: null,
            CollaborationService: null,
            RuntimeSkillStore: runtimeStore,
            EventSink: null,
            SetStatus: _ => { },
            BuildBaseContextAsync: (_, _) => Task.FromResult("base-context"),
            Logger: Mock.Of<ILogger>());

        var response = await processor.ProcessAsync(context, new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            FromAgentId = "lucifer",
            ToAgentId = "agent-42",
            Type = MessageType.Task,
            Content = "Produce the plan"
        }, CancellationToken.None);

        response.Content.Should().Be("final-answer");
        capturedLoop.CapturedContext.Should().NotBeNull();
        capturedLoop.CapturedContext!.Persona.AvailableTools.Should().Contain("request_collaboration");
        capturedLoop.CapturedContext.Persona.Specializations.Should().Contain("Risk arbitration");
        capturedLoop.CapturedContext.Persona.SystemPrompt.Should().Contain("Temporary Skill Guidance");
        capturedLoop.CapturedContext.Persona.CustomInstructions["runtime_skill_packs"].Should().Contain("critical-pack");
        capturedLoop.CapturedContext.SystemContext.Should().Contain("Allowed tools for this task");
        capturedLoop.CapturedContext.SystemContext.Should().Contain("request_collaboration");
    }

    [Fact]
    public async Task ProcessAsync_ShouldPersistReActCheckpoints_WithBranchAndCollaborationId()
    {
        var sharedMemory = new Mock<ISharedMemory>();
        sharedMemory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sharedMemory.Setup(x => x.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loopRunner = new CapturingLoopRunner(emitCheckpoint: true);

        var processor = new DefaultReActTaskProcessor(
            new DefaultRagContextEnricher(),
            new DefaultAgentEventAppender());

        var persona = new Persona
        {
            Name = "Agares",
            SystemPrompt = "You are Agares.",
            AvailableTools = new[] { "read_memory" },
            Specializations = new[] { "Planning" }
        };

        var context = new ReActTaskProcessorContext(
            AgentId: "agent-42",
            AgentName: "Agares",
            AgentRank: AgentRank.Duke,
            Persona: persona,
            LlmClient: Mock.Of<ILlmClient>(),
            ToolRegistry: Mock.Of<IToolRegistry>(),
            SharedMemory: sharedMemory.Object,
            ActionParser: Mock.Of<IActionParser>(),
            ActionExecutor: Mock.Of<IActionExecutor>(),
            ReportGenerator: Mock.Of<IReportGenerator>(),
            PromptBuilder: Mock.Of<IReActPromptBuilder>(),
            LoopRunner: loopRunner,
            ReActOptions: new ReActOptions { UseJsonResponse = true },
            RagOptions: new RagOptions { Enabled = false },
            VectorMemory: null,
            CollaborationService: null,
            RuntimeSkillStore: null,
            EventSink: null,
            SetStatus: _ => { },
            BuildBaseContextAsync: (_, _) => Task.FromResult("base-context"),
            Logger: Mock.Of<ILogger>());

        var task = new AgentMessage
        {
            Id = "branch-123",
            FromAgentId = "lucifer",
            ToAgentId = "agent-42",
            Type = MessageType.Task,
            Content = "Produce plan",
            Payload = new Dictionary<string, object>
            {
                ["collaboration_id"] = "collab-456"
            }
        };

        var response = await processor.ProcessAsync(context, task, CancellationToken.None);

        response.Content.Should().Be("final-answer");
        sharedMemory.Verify(
            x => x.AddFactAsync(
                It.Is<Fact>(f =>
                    f.Category == "react.checkpoint" &&
                    f.Content.Contains("branch-123") &&
                    f.Content.Contains("collab-456")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    private sealed class CapturingLoopRunner : IReActLoopRunner
    {
        private readonly bool _emitCheckpoint;

        public CapturingLoopRunner(bool emitCheckpoint = false)
        {
            _emitCheckpoint = emitCheckpoint;
        }

        public ReActLoopContext? CapturedContext { get; private set; }

        public async Task<ReActLoopResult> RunAsync(ReActLoopContext context, CancellationToken ct)
        {
            CapturedContext = context;

            if (_emitCheckpoint && context.EmitCheckpoint is not null)
            {
                await context.EmitCheckpoint(new ReActCheckpoint(
                    Phase: "plan",
                    Label: "test_checkpoint",
                    Detail: "checkpoint from test runner",
                    Iteration: 1,
                    OccurredAtUtc: DateTime.UtcNow), ct);
            }

            return new ReActLoopResult(
                FinalAnswer: "final-answer",
                Reasoning: "reasoning",
                Iterations: 1,
                ToolCalls: Array.Empty<string>());
        }
    }
}