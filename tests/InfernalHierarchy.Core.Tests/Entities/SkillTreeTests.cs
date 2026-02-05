using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using Xunit;

namespace InfernalHierarchy.Core.Tests.Entities;

public class SkillTreeTests
{
    [Fact]
    public void AgentSkillTree_GetOrCreateSkill_CreatesAndReturnsSameInstance()
    {
        var tree = new AgentSkillTree { AgentId = "a" };

        var s1 = tree.GetOrCreateSkill("tool");
        var s2 = tree.GetOrCreateSkill("tool");

        s1.Should().BeSameAs(s2);
        s1.ToolName.Should().Be("tool");
        tree.Skills.Should().ContainKey("tool");
    }

    [Fact]
    public void AgentSkillTree_AwardExperience_UpdatesTotals_AndLevelsUpWhenCrossingThreshold()
    {
        var tree = new AgentSkillTree { AgentId = "a" };

        var r1 = tree.AwardExperience("tool", 5);
        r1.LeveledUp.Should().BeFalse();
        r1.NewLevel.Should().Be(1);
        r1.NewMastery.Should().Be(MasteryLevel.Novice);
        tree.TotalExperiencePoints.Should().Be(5);

        var r2 = tree.AwardExperience("tool", 5);
        r2.LeveledUp.Should().BeTrue();
        r2.NewLevel.Should().Be(2);
        r2.NewMastery.Should().Be(MasteryLevel.Apprentice);
        r2.MasteryChanged.Should().BeTrue();
        tree.TotalExperiencePoints.Should().Be(10);
    }

    [Theory]
    [InlineData(MasteryLevel.Novice, 0.8)]
    [InlineData(MasteryLevel.Apprentice, 0.9)]
    [InlineData(MasteryLevel.Competent, 1.0)]
    [InlineData(MasteryLevel.Proficient, 1.15)]
    [InlineData(MasteryLevel.Expert, 1.3)]
    [InlineData(MasteryLevel.Master, 1.5)]
    public void AgentSkillTree_GetEfficiencyMultiplier_MapsMasteryLevels(MasteryLevel mastery, double expected)
    {
        var tree = new AgentSkillTree { AgentId = "a" };
        tree.GetEfficiencyMultiplier("unknown").Should().Be(1.0);

        tree.Skills["tool"] = new ToolSkill { ToolName = "tool", MasteryLevel = mastery };
        tree.GetEfficiencyMultiplier("tool").Should().Be(expected);
    }

    [Theory]
    [InlineData(MasteryLevel.Novice, 0.0)]
    [InlineData(MasteryLevel.Apprentice, 0.05)]
    [InlineData(MasteryLevel.Competent, 0.10)]
    [InlineData(MasteryLevel.Proficient, 0.15)]
    [InlineData(MasteryLevel.Expert, 0.20)]
    [InlineData(MasteryLevel.Master, 0.25)]
    public void AgentSkillTree_GetSuccessRateBonus_MapsMasteryLevels(MasteryLevel mastery, double expected)
    {
        var tree = new AgentSkillTree { AgentId = "a" };
        tree.GetSuccessRateBonus("unknown").Should().Be(0);

        tree.Skills["tool"] = new ToolSkill { ToolName = "tool", MasteryLevel = mastery };
        tree.GetSuccessRateBonus("tool").Should().Be(expected);
    }

    [Fact]
    public void ToolSkill_ComputedProperties_WorkAsExpected()
    {
        var skill = new ToolSkill { ToolName = "tool" };

        skill.SuccessRate.Should().Be(0);
        skill.ExperienceToNextLevel.Should().Be(10);
        skill.ProgressToNextLevel.Should().Be(0);

        skill.TimesUsed = 10;
        skill.SuccessfulUses = 7;
        skill.SuccessRate.Should().BeApproximately(0.7, 0.0001);

        skill.Level = 2;
        skill.ExperiencePoints = 20;
        skill.ExperienceToNextLevel.Should().Be(30);
        skill.ProgressToNextLevel.Should().BeApproximately(25.0, 0.0001);

        skill.Level = 10;
        skill.ProgressToNextLevel.Should().Be(100);
    }

    [Fact]
    public void ExperienceCalculator_CalculatesExperienceGain_AndFailurePenalty()
    {
        var xpFastFail = ExperienceCalculator.CalculateExperienceGain(success: false, executionTime: TimeSpan.FromMilliseconds(500));
        xpFastFail.Should().Be(3);

        var xpSuccess = ExperienceCalculator.CalculateExperienceGain(success: true, executionTime: TimeSpan.FromSeconds(100), complexity: 2);
        xpSuccess.Should().Be(30);

        ExperienceCalculator.CalculateFailurePenalty(MasteryLevel.Novice).Should().Be(5);
        ExperienceCalculator.CalculateFailurePenalty(MasteryLevel.Apprentice).Should().Be(3);
        ExperienceCalculator.CalculateFailurePenalty(MasteryLevel.Competent).Should().Be(2);
        ExperienceCalculator.CalculateFailurePenalty(MasteryLevel.Expert).Should().Be(1);
    }
}
