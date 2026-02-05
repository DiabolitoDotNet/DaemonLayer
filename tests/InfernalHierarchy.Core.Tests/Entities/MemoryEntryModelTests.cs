using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using Xunit;

namespace InfernalHierarchy.Core.Tests.Entities;

public class MemoryEntryModelTests
{
    [Fact]
    public void FactVersion_ShouldSupportRoundTripProperties()
    {
        var v = new FactVersion
        {
            VersionNumber = 2,
            Content = "new",
            Confidence = 0.5,
            ModifiedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedBy = "agent",
            ChangeReason = "correction"
        };

        v.VersionNumber.Should().Be(2);
        v.Content.Should().Be("new");
        v.Confidence.Should().Be(0.5);
        v.ModifiedAt.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        v.ModifiedBy.Should().Be("agent");
        v.ChangeReason.Should().Be("correction");
    }

    [Fact]
    public void SkillTreeStats_AndSummaries_ShouldInitializeCollections()
    {
        var stats = new InfernalHierarchy.Core.Interfaces.SkillTreeStats();

        stats.MasteryDistribution.Should().NotBeNull();
        stats.MostCommonSkills.Should().NotBeNull();
        stats.TopAgentsByExperience.Should().NotBeNull();

        var topSkill = new InfernalHierarchy.Core.Interfaces.TopSkillInfo { ToolName = "tool", AgentCount = 3, AverageLevel = 2.5 };
        var agentSummary = new InfernalHierarchy.Core.Interfaces.AgentSkillSummary { AgentId = "a", TotalExperience = 123, MasterSkillCount = 1, ExpertSkillCount = 2 };

        topSkill.ToolName.Should().Be("tool");
        agentSummary.AgentId.Should().Be("a");
    }
}
