using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace InfernalHierarchy.Tools;

/// <summary>
/// Tracks tool execution success rates and agent learning patterns
/// </summary>
public class AgentLearningService
{
    private readonly ILogger<AgentLearningService> _logger;
    private readonly ISkillTreeService? _skillTreeService;
    private readonly ConcurrentDictionary<string, ToolLearningStats> _toolStats = new();
    private readonly ConcurrentDictionary<string, AgentLearningProfile> _agentProfiles = new();

    public AgentLearningService(
        ILogger<AgentLearningService> logger,
        ISkillTreeService? skillTreeService = null)
    {
        _logger = logger;
        _skillTreeService = skillTreeService;
    }

    /// <summary>
    /// Record tool execution result for learning and skill progression
    /// </summary>
    public async Task RecordToolExecutionAsync(
        string agentId,
        string agentRank,
        string toolName,
        bool success,
        TimeSpan duration,
        int complexity = 1,
        CancellationToken ct = default)
    {
        // Update global tool stats
        var toolKey = $"{toolName}";
        var toolStats = _toolStats.GetOrAdd(toolKey, _ => new ToolLearningStats { ToolName = toolName });
        toolStats.RecordExecution(success, duration);

        // Update agent-specific tool proficiency
        var agentKey = $"{agentId}_{toolName}";
        var agentToolStats = _toolStats.GetOrAdd(agentKey, _ => new ToolLearningStats
        {
            ToolName = toolName,
            AgentId = agentId
        });
        agentToolStats.RecordExecution(success, duration);

        // Update agent learning profile
        var profile = _agentProfiles.GetOrAdd(agentId, _ => new AgentLearningProfile
        {
            AgentId = agentId,
            Rank = agentRank
        });
        profile.RecordToolUse(toolName, success);

        // Award skill tree experience points
        if (_skillTreeService != null)
        {
            try
            {
                var result = await _skillTreeService.AwardExperienceAsync(
                    agentId, toolName, success, duration, complexity, ct);
                
                if (result.LeveledUp || result.MasteryChanged)
                {
                    _logger.LogInformation(
                        "🎓 Agent {AgentId} skill progression in {Tool}: Level {Level}, Mastery {Mastery}",
                        agentId, toolName, result.NewLevel, result.NewMastery);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to award skill experience for agent {AgentId}", agentId);
            }
        }

        if (success && toolStats.SuccessRate > 0.9)
        {
            _logger.LogDebug("📈 Agent {AgentId} shows proficiency with {Tool} ({Rate:P0} success rate)",
                agentId, toolName, toolStats.SuccessRate);
        }
    }

    /// <summary>
    /// Record tool execution result for learning (synchronous version for backwards compatibility)
    /// </summary>
    public void RecordToolExecution(string agentId, string agentRank, string toolName, bool success, TimeSpan duration)
    {
        RecordToolExecutionAsync(agentId, agentRank, toolName, success, duration).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Get recommended tools for an agent based on success history
    /// </summary>
    public List<string> GetRecommendedTools(string agentId, IEnumerable<string> availableTools)
    {
        if (!_agentProfiles.TryGetValue(agentId, out var profile))
        {
            return availableTools.ToList();
        }

        return availableTools
            .OrderByDescending(tool =>
            {
                if (profile.ToolProficiency.TryGetValue(tool, out var proficiency))
                {
                    return proficiency.SuccessRate * proficiency.TotalUses; // Weighted by experience
                }
                return 0;
            })
            .ToList();
    }

    /// <summary>
    /// Get tool success rate globally
    /// </summary>
    public double GetToolSuccessRate(string toolName)
    {
        return _toolStats.TryGetValue(toolName, out var stats) ? stats.SuccessRate : 0;
    }

    /// <summary>
    /// Get agent proficiency with specific tool
    /// </summary>
    public double GetAgentToolProficiency(string agentId, string toolName)
    {
        if (_agentProfiles.TryGetValue(agentId, out var profile))
        {
            if (profile.ToolProficiency.TryGetValue(toolName, out var proficiency))
            {
                return proficiency.SuccessRate;
            }
        }
        return 0;
    }

    /// <summary>
    /// Get learning statistics for an agent
    /// </summary>
    public AgentLearningStats? GetAgentStats(string agentId)
    {
        if (!_agentProfiles.TryGetValue(agentId, out var profile))
        {
            return null;
        }

        var topTools = profile.ToolProficiency
            .OrderByDescending(kv => kv.Value.SuccessRate)
            .Take(5)
            .Select(kv => new ToolProficiencyInfo
            {
                ToolName = kv.Key,
                SuccessRate = kv.Value.SuccessRate,
                TotalUses = kv.Value.TotalUses
            })
            .ToList();

        return new AgentLearningStats
        {
            AgentId = agentId,
            Rank = profile.Rank,
            TotalToolExecutions = profile.TotalToolUses,
            TopTools = topTools,
            OverallSuccessRate = profile.ToolProficiency.Any()
                ? profile.ToolProficiency.Average(kv => kv.Value.SuccessRate)
                : 0
        };
    }

    /// <summary>
    /// Get all learning statistics
    /// </summary>
    public LearningSystemStats GetSystemStats()
    {
        return new LearningSystemStats
        {
            TotalAgentsTracked = _agentProfiles.Count,
            // Global tool stats are tracked with AgentId == null.
            // Do not rely on key naming, since tool names can contain '_' (e.g., web_search).
            TotalToolsTracked = _toolStats.Count(kv => kv.Value.AgentId == null),
            GlobalToolStats = _toolStats
                .Where(kv => kv.Value.AgentId == null)
                .Select(kv => new ToolStatsInfo
                {
                    ToolName = kv.Value.ToolName,
                    SuccessRate = kv.Value.SuccessRate,
                    TotalExecutions = kv.Value.TotalExecutions,
                    AverageDuration = kv.Value.AverageDuration
                })
                .OrderByDescending(t => t.TotalExecutions)
                .ToList()
        };
    }
}

/// <summary>
/// Learning statistics for a specific tool
/// </summary>
public class ToolLearningStats
{
    public string ToolName { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public int TotalExecutions { get; private set; }
    public int SuccessfulExecutions { get; private set; }
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions : 0;
    public TimeSpan TotalDuration { get; private set; }
    public TimeSpan AverageDuration => TotalExecutions > 0 ? TotalDuration / TotalExecutions : TimeSpan.Zero;

    public void RecordExecution(bool success, TimeSpan duration)
    {
        TotalExecutions++;
        if (success) SuccessfulExecutions++;
        TotalDuration += duration;
    }
}

/// <summary>
/// Learning profile for an agent
/// </summary>
public class AgentLearningProfile
{
    public string AgentId { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public Dictionary<string, ToolProficiency> ToolProficiency { get; set; } = new();
    public int TotalToolUses { get; private set; }

    public void RecordToolUse(string toolName, bool success)
    {
        TotalToolUses++;

        if (!ToolProficiency.ContainsKey(toolName))
        {
            ToolProficiency[toolName] = new ToolProficiency { ToolName = toolName };
        }

        ToolProficiency[toolName].RecordUse(success);
    }
}

/// <summary>
/// Tool proficiency tracking
/// </summary>
public class ToolProficiency
{
    public string ToolName { get; set; } = string.Empty;
    public int TotalUses { get; private set; }
    public int SuccessfulUses { get; private set; }
    public double SuccessRate => TotalUses > 0 ? (double)SuccessfulUses / TotalUses : 0;

    public void RecordUse(bool success)
    {
        TotalUses++;
        if (success) SuccessfulUses++;
    }
}

/// <summary>
/// Agent learning statistics
/// </summary>
public class AgentLearningStats
{
    public string AgentId { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public int TotalToolExecutions { get; set; }
    public double OverallSuccessRate { get; set; }
    public List<ToolProficiencyInfo> TopTools { get; set; } = new();
}

public class ToolProficiencyInfo
{
    public string ToolName { get; set; } = string.Empty;
    public double SuccessRate { get; set; }
    public int TotalUses { get; set; }
}

/// <summary>
/// System-wide learning statistics
/// </summary>
public class LearningSystemStats
{
    public int TotalAgentsTracked { get; set; }
    public int TotalToolsTracked { get; set; }
    public List<ToolStatsInfo> GlobalToolStats { get; set; } = new();
}

public class ToolStatsInfo
{
    public string ToolName { get; set; } = string.Empty;
    public double SuccessRate { get; set; }
    public int TotalExecutions { get; set; }
    public TimeSpan AverageDuration { get; set; }
}
