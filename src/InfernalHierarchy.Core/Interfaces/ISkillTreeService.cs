
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
    public int TotalAgentsWithSkills { get; set; }
    public int TotalSkillsTracked { get; set; }
    public Dictionary<MasteryLevel, int> MasteryDistribution { get; set; } = new();
    public List<TopSkillInfo> MostCommonSkills { get; set; } = new();
    public List<AgentSkillSummary> TopAgentsByExperience { get; set; } = new();
}

public class TopSkillInfo
{
    public string ToolName { get; set; } = string.Empty;
    public int AgentCount { get; set; }
    public double AverageLevel { get; set; }
}

public class AgentSkillSummary
{
    public string AgentId { get; set; } = string.Empty;
    public int TotalExperience { get; set; }
    public int MasterSkillCount { get; set; }
    public int ExpertSkillCount { get; set; }
}
