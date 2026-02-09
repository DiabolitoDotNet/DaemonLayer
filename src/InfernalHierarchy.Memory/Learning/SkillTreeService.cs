using System.Collections.Concurrent;
using System.Text.Json;

namespace InfernalHierarchy.Memory.Learning;

/// <summary>
/// Manages agent skill trees with LiteDB persistence
/// </summary>
public class SkillTreeService : ISkillTreeService
{
    private readonly ILogger<SkillTreeService> _logger;
    private readonly ISharedMemory _sharedMemory;
    private readonly ConcurrentDictionary<string, AgentSkillTree> _skillTreeCache = new();
    private const string SkillTreeCategory = "skill_tree";
    private static readonly JsonSerializerOptions SkillTreeJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SkillTreeService(
        ILogger<SkillTreeService> logger,
        ISharedMemory sharedMemory)
    {
        _logger = logger;
        _sharedMemory = sharedMemory;
    }

    /// <summary>
    /// Get or create skill tree for an agent
    /// </summary>
    public async Task<AgentSkillTree> GetSkillTreeAsync(string agentId, CancellationToken ct = default)
    {
        // Check cache first
        if (_skillTreeCache.TryGetValue(agentId, out var cachedTree))
        {
            return cachedTree;
        }

        // Try to load from memory
        var factKey = $"skill_tree_{agentId}";
        var fact = await _sharedMemory.GetFactAsync(factKey, ct);

        AgentSkillTree skillTree;
        if (fact != null)
        {
            if (string.IsNullOrWhiteSpace(fact.Content))
            {
                _logger.LogWarning("Skill tree fact {FactId} for agent {AgentId} had empty content; resetting.", fact.Id, agentId);
                skillTree = new AgentSkillTree { AgentId = agentId };
                await TrySelfHealSkillTreeFactAsync(skillTree, fact, ct).ConfigureAwait(false);
            }
            else
            {
                // Deserialize from stored fact (best-effort; corrupt JSON should not break learning)
                try
                {
                    skillTree = JsonSerializer.Deserialize<AgentSkillTree>(fact.Content, SkillTreeJsonOptions)
                               ?? new AgentSkillTree { AgentId = agentId };
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to deserialize skill tree for agent {AgentId} from fact {FactId} (len={Length}); resetting.",
                        agentId,
                        fact.Id,
                        fact.Content.Length);

                    skillTree = new AgentSkillTree { AgentId = agentId };
                    await TrySelfHealSkillTreeFactAsync(skillTree, fact, ct).ConfigureAwait(false);
                }
            }
        }
        else
        {
            // Create new skill tree
            skillTree = new AgentSkillTree { AgentId = agentId };
        }

        _skillTreeCache[agentId] = skillTree;
        return skillTree;
    }

    private async Task TrySelfHealSkillTreeFactAsync(AgentSkillTree skillTree, Fact existingFact, CancellationToken ct)
    {
        try
        {
            // Preserve the existing fact id, but ensure it remains categorized correctly.
            existingFact.Category = SkillTreeCategory;
            existingFact.Content = JsonSerializer.Serialize(skillTree, SkillTreeJsonOptions);
            existingFact.LastModifiedAt = DateTime.UtcNow;
            existingFact.LastModifiedBy = skillTree.AgentId;

            await _sharedMemory.UpdateFactAsync(existingFact, "Reset corrupt/empty skill tree", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort: do not fail agent operations if self-heal cannot persist.
            _logger.LogDebug(ex, "Failed to self-heal skill tree fact {FactId} for agent {AgentId}", existingFact.Id, skillTree.AgentId);
        }
    }

    /// <summary>
    /// Award experience points for tool usage
    /// </summary>
    public async Task<SkillProgressionResult> AwardExperienceAsync(
        string agentId,
        string toolName,
        bool success,
        TimeSpan executionTime,
        int complexity = 1,
        CancellationToken ct = default)
    {
        var skillTree = await GetSkillTreeAsync(agentId, ct);
        var skill = skillTree.GetOrCreateSkill(toolName);

        // Record usage statistics
        skill.TimesUsed++;
        if (success) skill.SuccessfulUses++;
        skill.LastUsed = DateTime.UtcNow;

        // Calculate experience gain
        var experienceGain = ExperienceCalculator.CalculateExperienceGain(success, executionTime, complexity);

        // Apply failure penalty if applicable
        if (!success)
        {
            var penalty = ExperienceCalculator.CalculateFailurePenalty(skill.MasteryLevel);
            experienceGain = Math.Max(1, experienceGain - penalty);
        }

        // Award experience and check for level up
        var result = skillTree.AwardExperience(toolName, experienceGain);

        // Persist to memory
        await PersistSkillTreeAsync(skillTree, ct);

        // Log progression events
        if (result.LeveledUp)
        {
            _logger.LogInformation(
                "🌟 Agent {AgentId} leveled up {Tool}: {OldLevel}→{NewLevel} ({Mastery})",
                agentId, toolName, result.OldLevel, result.NewLevel, skill.MasteryLevel);
        }

        if (result.MasteryChanged)
        {
            _logger.LogInformation(
                "✨ Agent {AgentId} mastery increased in {Tool}: {OldMastery}→{NewMastery}",
                agentId, toolName, result.OldMastery, result.NewMastery);
        }

        return result;
    }

    /// <summary>
    /// Get efficiency multiplier for a tool
    /// </summary>
    public async Task<double> GetEfficiencyMultiplierAsync(string agentId, string toolName, CancellationToken ct = default)
    {
        var skillTree = await GetSkillTreeAsync(agentId, ct);
        return skillTree.GetEfficiencyMultiplier(toolName);
    }

    /// <summary>
    /// Get success rate bonus for a tool
    /// </summary>
    public async Task<double> GetSuccessRateBonusAsync(string agentId, string toolName, CancellationToken ct = default)
    {
        var skillTree = await GetSkillTreeAsync(agentId, ct);
        return skillTree.GetSuccessRateBonus(toolName);
    }

    /// <summary>
    /// Get all agents with specific mastery level in a tool
    /// </summary>
    public async Task<List<string>> GetAgentsByMasteryAsync(
        string toolName,
        MasteryLevel minMastery,
        CancellationToken ct = default)
    {
        // Load all skill trees from memory
        var allFacts = await _sharedMemory.GetFactsByCategoryAsync(SkillTreeCategory, ct);

        var qualifiedAgents = new List<string>();

        foreach (var fact in allFacts)
        {
            try
            {
                var skillTree = JsonSerializer.Deserialize<AgentSkillTree>(fact.Content);
                if (skillTree != null &&
                    skillTree.Skills.TryGetValue(toolName, out var skill) &&
                    skill.MasteryLevel >= minMastery)
                {
                    qualifiedAgents.Add(skillTree.AgentId);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize skill tree from fact {FactId}", fact.Id);
            }
        }

        return qualifiedAgents;
    }

    /// <summary>
    /// Get skill recommendations for agent based on rank and specialization
    /// </summary>
    public async Task<List<string>> GetRecommendedSkillsAsync(
        string agentId,
        AgentRank rank,
        CancellationToken ct = default)
    {
        var skillTree = await GetSkillTreeAsync(agentId, ct);

        // Recommend based on rank
        var recommendations = rank switch
        {
            AgentRank.Supreme => new List<string>
            {
                "create_sub_agent",
                "read_memory",
                "write_memory",
                "web_search",
                "telegram_send"
            },
            AgentRank.Prince => new List<string>
            {
                "read_memory",
                "write_memory",
                "web_search",
                "create_sub_agent"
            },
            AgentRank.Duke => new List<string>
            {
                "read_memory",
                "write_memory",
                "web_search"
            },
            AgentRank.Worker => new List<string>
            {
                "read_memory",
                "web_search"
            },
            _ => new List<string>()
        };

        // Prioritize skills agent hasn't mastered yet
        return recommendations
            .OrderBy(tool =>
            {
                if (!skillTree.Skills.TryGetValue(tool, out var skill))
                {
                    return 0; // Not learned yet - high priority
                }
                return skill.MasteryLevel >= MasteryLevel.Expert ? 10 : (int)skill.MasteryLevel;
            })
            .ToList();
    }

    /// <summary>
    /// Get skill tree statistics
    /// </summary>
    public async Task<SkillTreeStats> GetStatsAsync(CancellationToken ct = default)
    {
        var allFacts = await _sharedMemory.GetFactsByCategoryAsync(SkillTreeCategory, ct);

        var stats = new SkillTreeStats
        {
            MasteryDistribution = new Dictionary<MasteryLevel, int>()
        };

        var allSkills = new Dictionary<string, List<ToolSkill>>();
        var agentExperience = new List<AgentSkillSummary>();

        foreach (var fact in allFacts)
        {
            try
            {
                var skillTree = JsonSerializer.Deserialize<AgentSkillTree>(fact.Content);
                if (skillTree == null) continue;

                stats.TotalAgentsWithSkills++;

                // Collect skills for analysis
                foreach (var (toolName, skill) in skillTree.Skills)
                {
                    if (!allSkills.ContainsKey(toolName))
                    {
                        allSkills[toolName] = new List<ToolSkill>();
                    }
                    allSkills[toolName].Add(skill);

                    // Count mastery distribution
                    if (!stats.MasteryDistribution.ContainsKey(skill.MasteryLevel))
                    {
                        stats.MasteryDistribution[skill.MasteryLevel] = 0;
                    }
                    stats.MasteryDistribution[skill.MasteryLevel]++;
                    stats.TotalSkillsTracked++;
                }

                // Track agent summary
                var masterCount = skillTree.Skills.Count(s => s.Value.MasteryLevel == MasteryLevel.Master);
                var expertCount = skillTree.Skills.Count(s => s.Value.MasteryLevel == MasteryLevel.Expert);

                agentExperience.Add(new AgentSkillSummary
                {
                    AgentId = skillTree.AgentId,
                    TotalExperience = skillTree.TotalExperiencePoints,
                    MasterSkillCount = masterCount,
                    ExpertSkillCount = expertCount
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize skill tree from fact {FactId}", fact.Id);
            }
        }

        // Calculate most common skills
        stats.MostCommonSkills = allSkills
            .Select(kv => new TopSkillInfo
            {
                ToolName = kv.Key,
                AgentCount = kv.Value.Count,
                AverageLevel = kv.Value.Average(s => s.Level)
            })
            .OrderByDescending(s => s.AgentCount)
            .Take(10)
            .ToList();

        // Top agents by experience
        stats.TopAgentsByExperience = agentExperience
            .OrderByDescending(a => a.TotalExperience)
            .Take(10)
            .ToList();

        return stats;
    }

    /// <summary>
    /// Persist skill tree to shared memory
    /// </summary>
    private async Task PersistSkillTreeAsync(AgentSkillTree skillTree, CancellationToken ct)
    {
        var factId = $"skill_tree_{skillTree.AgentId}";
        var existingFact = await _sharedMemory.GetFactAsync(factId, ct);

        var fact = existingFact ?? new Fact
        {
            Id = factId,
            Category = SkillTreeCategory,
            Source = "SkillTreeService",
            CreatedBy = skillTree.AgentId,
            Visibility = MemoryVisibility.Private,
            Confidence = 1.0
        };

        fact.Content = JsonSerializer.Serialize(skillTree);
        fact.LastModifiedAt = DateTime.UtcNow;
        fact.LastModifiedBy = skillTree.AgentId;

        if (existingFact == null)
        {
            await _sharedMemory.AddFactAsync(fact, ct);
        }
        else
        {
            await _sharedMemory.UpdateFactAsync(fact, "Skill progression update", ct);
        }
    }
}
