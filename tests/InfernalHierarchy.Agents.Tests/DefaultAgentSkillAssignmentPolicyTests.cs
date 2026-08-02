using FluentAssertions;
using InfernalHierarchy.Agents.Policies;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class DefaultAgentSkillAssignmentPolicyTests
{
    [Fact]
    public async Task EvaluateTemporarySkillRequestAsync_ShouldApprove_WhenRequestIsAllowed()
    {
        var catalog = new Mock<ISkillPackCatalog>();
        catalog.Setup(c => c.GetByIdAsync("safe-pack", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillPack
            {
                Id = "safe-pack",
                RiskLevel = "Low",
                Enabled = true,
                AllowedRanks = new[] { "Duke", "Prince" }
            });

        var options = Options.Create(new AgentSkillAssignmentOptions
        {
            Enabled = true,
            AllowSelfServiceSkillRequests = true,
            SelfServiceAllowedRanks = ["Duke", "Prince", "Supreme"],
            EscalateRiskLevelAtOrAbove = "High"
        });

        var policy = new DefaultAgentSkillAssignmentPolicy(catalog.Object, options, Mock.Of<ILogger<DefaultAgentSkillAssignmentPolicy>>());

        var decision = await policy.EvaluateTemporarySkillRequestAsync(new SkillAssignmentRequest
        {
            SkillPackId = "safe-pack",
            RequestorRank = AgentRank.Duke,
            TargetAgentRank = AgentRank.Duke
        });

        decision.IsApproved.Should().BeTrue();
        decision.RequiresEscalation.Should().BeFalse();
        decision.ReasonCode.Should().Be("approved");
    }

    [Fact]
    public async Task EvaluateTemporarySkillRequestAsync_ShouldDeny_WhenTargetRankNotAllowed()
    {
        var catalog = new Mock<ISkillPackCatalog>();
        catalog.Setup(c => c.GetByIdAsync("restricted-pack", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillPack
            {
                Id = "restricted-pack",
                RiskLevel = "Low",
                Enabled = true,
                AllowedRanks = new[] { "Supreme" }
            });

        var policy = new DefaultAgentSkillAssignmentPolicy(
            catalog.Object,
            Options.Create(new AgentSkillAssignmentOptions()),
            Mock.Of<ILogger<DefaultAgentSkillAssignmentPolicy>>());

        var decision = await policy.EvaluateTemporarySkillRequestAsync(new SkillAssignmentRequest
        {
            SkillPackId = "restricted-pack",
            RequestorRank = AgentRank.Prince,
            TargetAgentRank = AgentRank.Duke
        });

        decision.IsApproved.Should().BeFalse();
        decision.RequiresEscalation.Should().BeFalse();
        decision.ReasonCode.Should().Be("target_rank_not_allowed");
    }

    [Fact]
    public async Task EvaluateTemporarySkillRequestAsync_ShouldEscalate_WhenRiskIsHigh()
    {
        var catalog = new Mock<ISkillPackCatalog>();
        catalog.Setup(c => c.GetByIdAsync("high-risk", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkillPack
            {
                Id = "high-risk",
                RiskLevel = "High",
                Enabled = true,
                AllowedRanks = new[] { "Prince" }
            });

        var options = Options.Create(new AgentSkillAssignmentOptions
        {
            Enabled = true,
            AllowSelfServiceSkillRequests = true,
            SelfServiceAllowedRanks = ["Supreme", "Prince", "Duke"],
            EscalateRiskLevelAtOrAbove = "High"
        });

        var policy = new DefaultAgentSkillAssignmentPolicy(catalog.Object, options, Mock.Of<ILogger<DefaultAgentSkillAssignmentPolicy>>());

        var decision = await policy.EvaluateTemporarySkillRequestAsync(new SkillAssignmentRequest
        {
            SkillPackId = "high-risk",
            RequestorRank = AgentRank.Prince,
            TargetAgentRank = AgentRank.Prince
        });

        decision.IsApproved.Should().BeFalse();
        decision.RequiresEscalation.Should().BeTrue();
        decision.ReasonCode.Should().Be("high_risk_escalation");
    }
}
