using FluentAssertions;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Host.Agents;
using InfernalHierarchy.Memory.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class LiteDbAgentSkillRuntimeStoreTests
{
    [Fact]
    public void ApplyGrant_ShouldPersistAcrossStoreRecreation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-agent-skills-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "agent-skills.db");

        var options = Options.Create(new AgentSkillAssignmentOptions
        {
            RuntimeGrantDatabasePath = dbPath,
            RuntimeGrantMaxEntries = 100
        });

        var memoryOptions = Options.Create(new MemoryOptions { DatabasePath = dbPath });

        var now = DateTime.UtcNow;

        using (var store = new LiteDbAgentSkillRuntimeStore(options, memoryOptions, NullLogger<LiteDbAgentSkillRuntimeStore>.Instance))
        {
            store.ApplyGrant("agent-1", new AgentSkillGrant
            {
                SkillPackId = "team-coordination",
                ExpiresAtUtc = now.AddMinutes(20),
                AdditionalTools = new[] { "request_collaboration" },
                AdditionalSpecializations = new[] { "Coordination" },
                PromptFragments = new[] { "Escalate uncertain branches." }
            });
        }

        using var reopened = new LiteDbAgentSkillRuntimeStore(options, memoryOptions, NullLogger<LiteDbAgentSkillRuntimeStore>.Instance);
        var overlay = reopened.GetOverlay("agent-1", now);

        overlay.ActiveSkillPackIds.Should().Contain("team-coordination");
        overlay.AdditionalTools.Should().Contain("request_collaboration");
        overlay.AdditionalSpecializations.Should().Contain("Coordination");
        overlay.PromptFragments.Should().Contain("Escalate uncertain branches.");
    }

    [Fact]
    public void PruneExpired_ShouldRemoveExpiredGrants()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "infernal-agent-skills-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "agent-skills.db");

        var options = Options.Create(new AgentSkillAssignmentOptions
        {
            RuntimeGrantDatabasePath = dbPath,
            RuntimeGrantMaxEntries = 100
        });

        var memoryOptions = Options.Create(new MemoryOptions { DatabasePath = dbPath });

        using var store = new LiteDbAgentSkillRuntimeStore(options, memoryOptions, NullLogger<LiteDbAgentSkillRuntimeStore>.Instance);
        var now = DateTime.UtcNow;

        store.ApplyGrant("agent-2", new AgentSkillGrant
        {
            SkillPackId = "expired-pack",
            ExpiresAtUtc = now.AddMinutes(-5),
            AdditionalTools = new[] { "web_search" }
        });

        var removed = store.PruneExpired(now);
        var overlay = store.GetOverlay("agent-2", now);

        removed.Should().BeGreaterOrEqualTo(0);
        overlay.ActiveSkillPackIds.Should().BeEmpty();
    }
}
