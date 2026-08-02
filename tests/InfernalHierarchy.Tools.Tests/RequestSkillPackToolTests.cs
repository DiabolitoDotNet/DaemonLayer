using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Tools.Tools.Agent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class RequestSkillPackToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldApproveAndApplyGrant_WhenPolicyApproves()
    {
        var catalog = new Mock<ISkillPackCatalog>();
        catalog.Setup(x => x.GetByIdAsync("impl-pack", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillPack
            {
                Id = "impl-pack",
                Enabled = true,
                AdditionalTools = new[] { "web_search" },
                AdditionalSpecializations = new[] { "Implementation" },
                PromptFragments = new[] { "Use incremental verification." }
            });

        var policy = new Mock<IAgentSkillAssignmentPolicy>();
        policy.Setup(x => x.EvaluateTemporarySkillRequestAsync(It.IsAny<SkillAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillAssignmentDecision.Approved("approved", "ok"));

        var runtimeStore = new Mock<IAgentSkillRuntimeStore>();
        runtimeStore.Setup(x => x.GetOverlay("agent-1", It.IsAny<DateTime>()))
            .Returns(new AgentSkillRuntimeOverlay
            {
                ActiveSkillPackIds = new[] { "impl-pack" },
                AdditionalTools = new[] { "web_search" },
                AdditionalSpecializations = new[] { "Implementation" }
            });

        var tool = new RequestSkillPackTool(
            Mock.Of<ILogger<RequestSkillPackTool>>(),
            catalog.Object,
            policy.Object,
            runtimeStore.Object,
            options: Microsoft.Extensions.Options.Options.Create(new AgentSkillAssignmentOptions()),
            eventSink: null);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["skill_pack_id"] = "impl-pack",
            ["reason"] = "Need implementation support",
            ["agent_id"] = "agent-1",
            ["agent_rank"] = "Duke"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("approved and applied");
        runtimeStore.Verify(x => x.ApplyGrant("agent-1", It.IsAny<AgentSkillGrant>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDeny_WhenPolicyDenies()
    {
        var catalog = Mock.Of<ISkillPackCatalog>();
        var policy = new Mock<IAgentSkillAssignmentPolicy>();
        policy.Setup(x => x.EvaluateTemporarySkillRequestAsync(It.IsAny<SkillAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillAssignmentDecision.Denied("target_rank_not_allowed", "not allowed"));

        var runtimeStore = new Mock<IAgentSkillRuntimeStore>();

        var tool = new RequestSkillPackTool(
            Mock.Of<ILogger<RequestSkillPackTool>>(),
            catalog,
            policy.Object,
            runtimeStore.Object,
            options: Microsoft.Extensions.Options.Options.Create(new AgentSkillAssignmentOptions()),
            eventSink: null);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["skill_pack_id"] = "restricted",
            ["reason"] = "Need it",
            ["agent_id"] = "agent-2",
            ["agent_rank"] = "Worker"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not allowed");
        runtimeStore.Verify(x => x.ApplyGrant(It.IsAny<string>(), It.IsAny<AgentSkillGrant>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAutoApproveEscalation_WhenConfigured()
    {
        var catalog = new Mock<ISkillPackCatalog>();
        catalog.Setup(x => x.GetByIdAsync("critical-pack", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillPack
            {
                Id = "critical-pack",
                Enabled = true,
                AdditionalTools = new[] { "request_collaboration" },
                AdditionalSpecializations = new[] { "Risk arbitration" },
                PromptFragments = new[] { "Escalate uncertain outcomes." }
            });

        var policy = new Mock<IAgentSkillAssignmentPolicy>();
        policy.Setup(x => x.EvaluateTemporarySkillRequestAsync(It.IsAny<SkillAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillAssignmentDecision.EscalationRequired("high_risk_escalation", "manager approval required"));

        var runtimeStore = new Mock<IAgentSkillRuntimeStore>();
        runtimeStore.Setup(x => x.GetOverlay("agent-3", It.IsAny<DateTime>()))
            .Returns(new AgentSkillRuntimeOverlay
            {
                ActiveSkillPackIds = new[] { "critical-pack" },
                AdditionalTools = new[] { "request_collaboration" }
            });

        var tool = new RequestSkillPackTool(
            Mock.Of<ILogger<RequestSkillPackTool>>(),
            catalog.Object,
            policy.Object,
            runtimeStore.Object,
            options: Microsoft.Extensions.Options.Options.Create(new AgentSkillAssignmentOptions
            {
                AutoApproveEscalationsByMainAgent = true,
                MainAgentId = "lucifer"
            }),
            eventSink: null);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["skill_pack_id"] = "critical-pack",
            ["reason"] = "Need critical capability",
            ["agent_id"] = "agent-3",
            ["agent_rank"] = "Prince"
        });

        result.Success.Should().BeTrue();
        result.Metadata["decision"].Should().Be("approved");
        result.Metadata["reason_code"].Should().Be("auto_approved_by_main_agent");
        runtimeStore.Verify(x => x.ApplyGrant("agent-3", It.IsAny<AgentSkillGrant>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAutoApproveEscalation_ByDefaultWhenOptionsOmitted()
    {
        var catalog = new Mock<ISkillPackCatalog>();
        catalog.Setup(x => x.GetByIdAsync("critical-pack", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillPack
            {
                Id = "critical-pack",
                Enabled = true,
                AdditionalTools = new[] { "request_collaboration" }
            });

        var policy = new Mock<IAgentSkillAssignmentPolicy>();
        policy.Setup(x => x.EvaluateTemporarySkillRequestAsync(It.IsAny<SkillAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillAssignmentDecision.EscalationRequired("high_risk_escalation", "manager approval required"));

        var runtimeStore = new Mock<IAgentSkillRuntimeStore>();
        runtimeStore.Setup(x => x.GetOverlay("agent-4", It.IsAny<DateTime>()))
            .Returns(new AgentSkillRuntimeOverlay
            {
                ActiveSkillPackIds = new[] { "critical-pack" },
                AdditionalTools = new[] { "request_collaboration" }
            });

        var tool = new RequestSkillPackTool(
            Mock.Of<ILogger<RequestSkillPackTool>>(),
            catalog.Object,
            policy.Object,
            runtimeStore.Object,
            options: null,
            eventSink: null);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["skill_pack_id"] = "critical-pack",
            ["reason"] = "Need critical capability",
            ["agent_id"] = "agent-4",
            ["agent_rank"] = "Prince"
        });

        result.Success.Should().BeTrue();
        result.Metadata["decision"].Should().Be("approved");
        result.Metadata["reason_code"].Should().Be("auto_approved_by_main_agent");
        runtimeStore.Verify(x => x.ApplyGrant("agent-4", It.IsAny<AgentSkillGrant>()), Times.Once);
    }
}
