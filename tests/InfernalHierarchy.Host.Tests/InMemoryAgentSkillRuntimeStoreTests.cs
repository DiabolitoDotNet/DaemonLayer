using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Host.Agents;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class InMemoryAgentSkillRuntimeStoreTests
{
    [Fact]
    public void GetOverlay_ShouldReturnMergedActiveGrants()
    {
        var store = new InMemoryAgentSkillRuntimeStore();
        var now = DateTime.UtcNow;

        store.ApplyGrant("agent-1", new AgentSkillGrant
        {
            SkillPackId = "pack-a",
            ExpiresAtUtc = now.AddMinutes(20),
            AdditionalTools = new[] { "web_search" },
            AdditionalSpecializations = new[] { "Search" },
            PromptFragments = new[] { "Check source quality." }
        });

        store.ApplyGrant("agent-1", new AgentSkillGrant
        {
            SkillPackId = "pack-b",
            ExpiresAtUtc = now.AddMinutes(10),
            AdditionalTools = new[] { "request_collaboration" },
            AdditionalSpecializations = new[] { "Coordination" },
            PromptFragments = new[] { "Escalate low confidence." }
        });

        var overlay = store.GetOverlay("agent-1", now);

        overlay.ActiveSkillPackIds.Should().BeEquivalentTo(new[] { "pack-a", "pack-b" });
        overlay.AdditionalTools.Should().Contain(new[] { "web_search", "request_collaboration" });
        overlay.AdditionalSpecializations.Should().Contain(new[] { "Search", "Coordination" });
        overlay.PromptFragments.Should().Contain(new[] { "Check source quality.", "Escalate low confidence." });
    }

    [Fact]
    public void GetOverlay_ShouldPruneExpiredGrants()
    {
        var store = new InMemoryAgentSkillRuntimeStore();
        var now = DateTime.UtcNow;

        store.ApplyGrant("agent-2", new AgentSkillGrant
        {
            SkillPackId = "expired-pack",
            ExpiresAtUtc = now.AddMinutes(-1),
            AdditionalTools = new[] { "web_search" }
        });

        var overlay = store.GetOverlay("agent-2", now);

        overlay.ActiveSkillPackIds.Should().BeEmpty();
        overlay.AdditionalTools.Should().BeEmpty();
    }
}
