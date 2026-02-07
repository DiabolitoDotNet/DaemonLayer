
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Service for managing agent skill trees and progression
/// </summary>
public interface ISkillTreeService
{
    /// <summary>
    /// Get or create skill tree for an agent
    /// </summary>
    Task<AgentSkillTree> GetSkillTreeAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Award experience points for tool usage
    /// </summary>
    Task<SkillProgressionResult> AwardExperienceAsync(
        string agentId,
        string toolName,
        bool success,
        TimeSpan executionTime,
        int complexity = 1,
        CancellationToken ct = default);

    /// <summary>
    /// Get efficiency multiplier for a tool
    /// </summary>
    Task<double> GetEfficiencyMultiplierAsync(string agentId, string toolName, CancellationToken ct = default);

    /// <summary>
    /// Get success rate bonus for a tool
    /// </summary>
    Task<double> GetSuccessRateBonusAsync(string agentId, string toolName, CancellationToken ct = default);

    /// <summary>
    /// Get all agents with specific mastery level in a tool
    /// </summary>
    Task<List<string>> GetAgentsByMasteryAsync(string toolName, MasteryLevel minMastery, CancellationToken ct = default);

    /// <summary>
    /// Get skill recommendations for agent based on rank and specialization
    /// </summary>
    Task<List<string>> GetRecommendedSkillsAsync(string agentId, AgentRank rank, CancellationToken ct = default);

    /// <summary>
    /// Get skill tree statistics
    /// </summary>
    Task<SkillTreeStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// Overall skill tree system statistics
/// </summary>
public class SkillTreeStats
{
    /// <summary>
    /// Number of agents that have at least one tracked skill.
    /// </summary>
    public int TotalAgentsWithSkills { get; set; }

    /// <summary>
    /// Number of distinct skills/tools being tracked.
    /// </summary>
    public int TotalSkillsTracked { get; set; }

    /// <summary>
    /// Distribution of mastery levels across all tracked skills.
    /// </summary>
    public Dictionary<MasteryLevel, int> MasteryDistribution { get; set; } = new();

    /// <summary>
    /// Most common skills/tools and their usage summary.
    /// </summary>
    public List<TopSkillInfo> MostCommonSkills { get; set; } = new();

    /// <summary>
    /// Agents with the highest total accumulated experience.
    /// </summary>
    public List<AgentSkillSummary> TopAgentsByExperience { get; set; } = new();
}

/// <summary>
/// Summary statistics for a particular skill/tool.
/// </summary>
public class TopSkillInfo
{
    /// <summary>
    /// Tool name the skill corresponds to.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Number of agents that have this skill tracked.
    /// </summary>
    public int AgentCount { get; set; }

    /// <summary>
    /// Average skill level across agents.
    /// </summary>
    public double AverageLevel { get; set; }
}

/// <summary>
/// Summary of an agent's skill progression.
/// </summary>
public class AgentSkillSummary
{
    /// <summary>
    /// Agent id.
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Total experience accumulated across all skills.
    /// </summary>
    public int TotalExperience { get; set; }

    /// <summary>
    /// Count of skills at Master level.
    /// </summary>
    public int MasterSkillCount { get; set; }

    /// <summary>
    /// Count of skills at Expert level.
    /// </summary>
    public int ExpertSkillCount { get; set; }
}
