using System.Collections.Concurrent;
using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Tools.Learning;

/// <summary>
/// Tracks tool execution success rates and agent learning patterns
/// </summary>
public class AgentLearningService
{
    private readonly ILogger<AgentLearningService> _logger;
    private readonly ISkillTreeService? _skillTreeService;
    private readonly ConcurrentDictionary<string, ToolLearningStats> _toolStats = new();
    private readonly ConcurrentDictionary<string, AgentLearningProfile> _agentProfiles = new();
    private readonly ConcurrentDictionary<string, CollaborationStrategyLearningStats> _collaborationStrategyStats = new();

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
            OverallSuccessRate = profile.ToolProficiency.Count > 0
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

    /// <summary>
    /// Record collaboration strategy outcome for adaptive strategy selection.
    /// </summary>
    public void RecordCollaborationStrategyOutcome(
        string agentId,
        string agentRank,
        CollaborationStrategy strategy,
        bool success,
        double confidence,
        double agreement,
        double averageLatencyMs,
        int rounds,
        int participants)
    {
        var strategyName = strategy.ToString();

        var globalKey = $"strategy:{strategyName}";
        var globalStats = _collaborationStrategyStats.GetOrAdd(globalKey, _ =>
            new CollaborationStrategyLearningStats(strategyName, agentId: null));

        globalStats.RecordOutcome(success, confidence, agreement, averageLatencyMs, rounds, participants);

        var agentKey = $"agent:{agentId}:strategy:{strategyName}";
        var agentStats = _collaborationStrategyStats.GetOrAdd(agentKey, _ =>
            new CollaborationStrategyLearningStats(strategyName, agentId));

        agentStats.RecordOutcome(success, confidence, agreement, averageLatencyMs, rounds, participants);

        // Keep compatibility with existing tool-centric telemetry and ranking surfaces.
        RecordToolExecution(
            agentId,
            agentRank,
            toolName: $"collaboration_strategy_{strategyName.ToLowerInvariant()}",
            success,
            duration: TimeSpan.FromMilliseconds(Math.Max(0, averageLatencyMs)));
    }

    /// <summary>
    /// Try to select the historically strongest strategy among candidates for an agent.
    /// </summary>
    public bool TryGetBestCollaborationStrategy(
        string agentId,
        IReadOnlyCollection<CollaborationStrategy> candidates,
        out CollaborationStrategy bestStrategy,
        out double bestScore)
    {
        bestStrategy = default;
        bestScore = 0;

        if (candidates.Count == 0)
        {
            return false;
        }

        var hasData = false;

        foreach (var candidate in candidates)
        {
            var candidateScore = GetCollaborationStrategyScore(agentId, candidate, out var sampleCount);
            if (sampleCount <= 0)
            {
                continue;
            }

            hasData = true;
            if (candidateScore > bestScore)
            {
                bestScore = candidateScore;
                bestStrategy = candidate;
            }
        }

        return hasData;
    }

    private double GetCollaborationStrategyScore(string agentId, CollaborationStrategy strategy, out int sampleCount)
    {
        var strategyName = strategy.ToString();
        var agentKey = $"agent:{agentId}:strategy:{strategyName}";
        var globalKey = $"strategy:{strategyName}";

        _collaborationStrategyStats.TryGetValue(agentKey, out var agentStats);
        _collaborationStrategyStats.TryGetValue(globalKey, out var globalStats);

        sampleCount = (agentStats?.TotalExecutions ?? 0) + (globalStats?.TotalExecutions ?? 0);
        if (sampleCount <= 0)
        {
            return 0;
        }

        var agentScore = agentStats?.CompositeScore ?? 0;
        var globalScore = globalStats?.CompositeScore ?? 0;

        // Prioritize agent-local behavior, with global signal as fallback guidance.
        return (agentScore * 0.7) + (globalScore * 0.3);
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

        if (!ToolProficiency.TryGetValue(toolName, out var proficiency))
        {
            proficiency = new ToolProficiency { ToolName = toolName };
            ToolProficiency[toolName] = proficiency;
        }

        proficiency.RecordUse(success);
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

public sealed class CollaborationStrategyLearningStats
{
    private readonly object _sync = new();
    private double _totalConfidence;
    private double _totalAgreement;
    private double _totalLatencyMs;
    private double _totalRounds;
    private double _totalParticipants;

    public CollaborationStrategyLearningStats(string strategyName, string? agentId)
    {
        StrategyName = strategyName;
        AgentId = agentId;
    }

    public string StrategyName { get; }
    public string? AgentId { get; }
    public int TotalExecutions { get; private set; }
    public int SuccessfulExecutions { get; private set; }
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions : 0;
    public double AverageConfidence => TotalExecutions > 0 ? _totalConfidence / TotalExecutions : 0;
    public double AverageAgreement => TotalExecutions > 0 ? _totalAgreement / TotalExecutions : 0;
    public double AverageLatencyMs => TotalExecutions > 0 ? _totalLatencyMs / TotalExecutions : 0;
    public double AverageRounds => TotalExecutions > 0 ? _totalRounds / TotalExecutions : 0;
    public double AverageParticipants => TotalExecutions > 0 ? _totalParticipants / TotalExecutions : 0;

    public double CompositeScore
    {
        get
        {
            var latencyPenalty = AverageLatencyMs <= 0
                ? 1.0
                : Math.Max(0.1, 1.0 - (AverageLatencyMs / 10_000.0));

            return (SuccessRate * 0.5)
                + (AverageConfidence * 0.2)
                + (AverageAgreement * 0.2)
                + (latencyPenalty * 0.1);
        }
    }

    public void RecordOutcome(
        bool success,
        double confidence,
        double agreement,
        double averageLatencyMs,
        int rounds,
        int participants)
    {
        lock (_sync)
        {
            TotalExecutions++;
            if (success)
            {
                SuccessfulExecutions++;
            }

            _totalConfidence += Math.Max(0.0, Math.Min(1.0, confidence));
            _totalAgreement += Math.Max(0.0, Math.Min(1.0, agreement));
            _totalLatencyMs += Math.Max(0.0, averageLatencyMs);
            _totalRounds += Math.Max(1, rounds);
            _totalParticipants += Math.Max(1, participants);
        }
    }
}
