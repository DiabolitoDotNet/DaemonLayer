namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Represents an agent's skill tree with progression through experience
/// </summary>
public class AgentSkillTree
{
    public string AgentId { get; set; } = string.Empty;
    public Dictionary<string, ToolSkill> Skills { get; set; } = new();
    public int TotalExperiencePoints { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Get or create skill for a tool
    /// </summary>
    public ToolSkill GetOrCreateSkill(string toolName)
    {
        if (!Skills.TryGetValue(toolName, out var skill))
        {
            skill = new ToolSkill { ToolName = toolName };
            Skills[toolName] = skill;
        }

        return skill;
    }

    /// <summary>
    /// Award experience points and handle level progression
    /// </summary>
    public SkillProgressionResult AwardExperience(string toolName, int experiencePoints)
    {
        var skill = GetOrCreateSkill(toolName);
        var oldLevel = skill.Level;
        var oldMastery = skill.MasteryLevel;

        skill.ExperiencePoints += experiencePoints;
        TotalExperiencePoints += experiencePoints;
        LastUpdated = DateTime.UtcNow;

        // Calculate new level based on experience curve
        var newLevel = CalculateLevel(skill.ExperiencePoints);
        var leveledUp = newLevel > oldLevel;
        
        if (leveledUp)
        {
            skill.Level = newLevel;
            skill.MasteryLevel = DetermineMasteryLevel(newLevel);
        }

        return new SkillProgressionResult
        {
            ToolName = toolName,
            OldLevel = oldLevel,
            NewLevel = skill.Level,
            LeveledUp = leveledUp,
            OldMastery = oldMastery,
            NewMastery = skill.MasteryLevel,
            MasteryChanged = skill.MasteryLevel != oldMastery,
            TotalExperience = skill.ExperiencePoints
        };
    }

    /// <summary>
    /// Calculate level based on experience points (logarithmic curve)
    /// </summary>
    private static int CalculateLevel(int experience)
    {
        if (experience < 10) return 1;
        if (experience < 50) return 2;
        if (experience < 150) return 3;
        if (experience < 350) return 4;
        if (experience < 700) return 5;
        if (experience < 1200) return 6;
        if (experience < 1800) return 7;
        if (experience < 2500) return 8;
        if (experience < 3500) return 9;
        return 10; // Max level
    }

    /// <summary>
    /// Determine mastery level based on skill level
    /// </summary>
    private static MasteryLevel DetermineMasteryLevel(int level)
    {
        return level switch
        {
            1 => MasteryLevel.Novice,
            2 or 3 => MasteryLevel.Apprentice,
            4 or 5 => MasteryLevel.Competent,
            6 or 7 => MasteryLevel.Proficient,
            8 or 9 => MasteryLevel.Expert,
            10 => MasteryLevel.Master,
            _ => MasteryLevel.Novice
        };
    }

    /// <summary>
    /// Get efficiency multiplier based on mastery level
    /// </summary>
    public double GetEfficiencyMultiplier(string toolName)
    {
        if (!Skills.TryGetValue(toolName, out var skill))
        {
            return 1.0; // Base efficiency
        }

        return skill.MasteryLevel switch
        {
            MasteryLevel.Novice => 0.8,      // -20% efficiency
            MasteryLevel.Apprentice => 0.9,   // -10% efficiency
            MasteryLevel.Competent => 1.0,    // Baseline
            MasteryLevel.Proficient => 1.15,  // +15% efficiency
            MasteryLevel.Expert => 1.3,       // +30% efficiency
            MasteryLevel.Master => 1.5,       // +50% efficiency
            _ => 1.0
        };
    }

    /// <summary>
    /// Get success rate bonus based on mastery
    /// </summary>
    public double GetSuccessRateBonus(string toolName)
    {
        if (!Skills.TryGetValue(toolName, out var skill))
        {
            return 0;
        }

        return skill.MasteryLevel switch
        {
            MasteryLevel.Novice => 0,
            MasteryLevel.Apprentice => 0.05,   // +5%
            MasteryLevel.Competent => 0.10,    // +10%
            MasteryLevel.Proficient => 0.15,   // +15%
            MasteryLevel.Expert => 0.20,       // +20%
            MasteryLevel.Master => 0.25,       // +25%
            _ => 0
        };
    }
}

/// <summary>
/// Individual tool skill with progression
/// </summary>
public class ToolSkill
{
    public string ToolName { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public MasteryLevel MasteryLevel { get; set; } = MasteryLevel.Novice;
    public int ExperiencePoints { get; set; }
    public int TimesUsed { get; set; }
    public int SuccessfulUses { get; set; }
    public DateTime FirstUsed { get; set; } = DateTime.UtcNow;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Calculate current success rate
    /// </summary>
    public double SuccessRate => TimesUsed > 0 ? (double)SuccessfulUses / TimesUsed : 0;

    /// <summary>
    /// Calculate experience needed for next level
    /// </summary>
    public int ExperienceToNextLevel => CalculateExperienceForLevel(Level + 1) - ExperiencePoints;

    /// <summary>
    /// Progress percentage to next level
    /// </summary>
    public double ProgressToNextLevel
    {
        get
        {
            if (Level >= 10) return 100; // Max level
            
            var currentLevelXp = CalculateExperienceForLevel(Level);
            var nextLevelXp = CalculateExperienceForLevel(Level + 1);
            var xpIntoLevel = ExperiencePoints - currentLevelXp;
            var xpNeededForLevel = nextLevelXp - currentLevelXp;
            
            return xpNeededForLevel > 0 ? (double)xpIntoLevel / xpNeededForLevel * 100 : 100;
        }
    }

    private static int CalculateExperienceForLevel(int level)
    {
        return level switch
        {
            1 => 0,
            2 => 10,
            3 => 50,
            4 => 150,
            5 => 350,
            6 => 700,
            7 => 1200,
            8 => 1800,
            9 => 2500,
            10 => 3500,
            _ => 3500
        };
    }
}

/// <summary>
/// Mastery levels for tool proficiency
/// </summary>
public enum MasteryLevel
{
    Novice,      // Just started, prone to errors
    Apprentice,  // Learning the basics
    Competent,   // Can handle routine tasks
    Proficient,  // Highly capable, consistent results
    Expert,      // Advanced understanding, innovative usage
    Master       // Complete mastery, teaching others
}

/// <summary>
/// Result of skill progression event
/// </summary>
public class SkillProgressionResult
{
    public string ToolName { get; set; } = string.Empty;
    public int OldLevel { get; set; }
    public int NewLevel { get; set; }
    public bool LeveledUp { get; set; }
    public MasteryLevel OldMastery { get; set; }
    public MasteryLevel NewMastery { get; set; }
    public bool MasteryChanged { get; set; }
    public int TotalExperience { get; set; }
}

/// <summary>
/// Experience calculation helper
/// </summary>
public static class ExperienceCalculator
{
    /// <summary>
    /// Calculate experience points awarded for tool execution
    /// </summary>
    public static int CalculateExperienceGain(bool success, TimeSpan executionTime, int complexity = 1)
    {
        var baseXp = success ? 10 : 3; // Failure still gives some XP
        var complexityMultiplier = complexity; // 1-5 scale
        var timeBonus = executionTime.TotalSeconds > 1 ? Math.Min(5, (int)(executionTime.TotalSeconds / 10)) : 0;
        
        return (baseXp + timeBonus) * complexityMultiplier;
    }

    /// <summary>
    /// Calculate experience penalty for failure
    /// </summary>
    public static int CalculateFailurePenalty(MasteryLevel currentMastery)
    {
        // Higher mastery = less penalty (you learn from mistakes)
        return currentMastery switch
        {
            MasteryLevel.Novice => 5,
            MasteryLevel.Apprentice => 3,
            MasteryLevel.Competent => 2,
            _ => 1
        };
    }
}
