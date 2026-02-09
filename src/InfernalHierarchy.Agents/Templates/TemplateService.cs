using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using InfernalHierarchy.Core.Serialization;

namespace InfernalHierarchy.Agents.Templates;

/// <summary>
/// Service for managing agent templates with JSON storage and in-memory caching
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly ILogger<TemplateService> _logger;
    private readonly IAgentFactory _agentFactory;
    private readonly ISkillTreeService _skillTreeService;
    private readonly string _templatesDirectory;
    private readonly ConcurrentDictionary<string, AgentTemplate> _templateCache;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public TemplateService(
        ILogger<TemplateService> logger,
        IAgentFactory agentFactory,
        ISkillTreeService skillTreeService,
        string templatesDirectory = "./templates")
    {
        _logger = logger;
        _agentFactory = agentFactory;
        _skillTreeService = skillTreeService;
        _templatesDirectory = templatesDirectory;
        _templateCache = new ConcurrentDictionary<string, AgentTemplate>();

        // Ensure templates directory exists
        if (!Directory.Exists(_templatesDirectory))
        {
            Directory.CreateDirectory(_templatesDirectory);
            _logger.LogInformation("📁 Created templates directory: {Directory}", _templatesDirectory);
        }
    }

    public async Task<AgentTemplate?> GetTemplateAsync(string templateId, CancellationToken ct = default)
    {
        // Check cache first
        if (_templateCache.TryGetValue(templateId, out var cachedTemplate))
        {
            _logger.LogDebug("✅ Template {TemplateId} retrieved from cache", templateId);
            return cachedTemplate;
        }

        // Load from disk
        await EnsureTemplatesLoadedAsync(ct);
        return _templateCache.TryGetValue(templateId, out var template) ? template : null;
    }

    public async Task<IEnumerable<AgentTemplate>> GetAllTemplatesAsync(CancellationToken ct = default)
    {
        await EnsureTemplatesLoadedAsync(ct);
        return _templateCache.Values.OrderBy(t => t.Category).ThenBy(t => t.Name);
    }

    public async Task<IEnumerable<AgentTemplate>> GetTemplatesByCategoryAsync(
        TemplateCategory category,
        CancellationToken ct = default)
    {
        await EnsureTemplatesLoadedAsync(ct);
        return _templateCache.Values
            .Where(t => t.Category == category)
            .OrderBy(t => t.Name);
    }

    public async Task<IEnumerable<AgentTemplate>> SearchTemplatesAsync(
        string query,
        CancellationToken ct = default)
    {
        await EnsureTemplatesLoadedAsync(ct);
        var lowerQuery = query.ToLowerInvariant();

        return _templateCache.Values.Where(t =>
            t.Name.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
            t.Description.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
            t.Tags.Any(tag => tag.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)) ||
            t.TemplateId.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)
        ).OrderByDescending(t =>
        {
            // Relevance scoring: exact match > name match > description match > tag match
            if (t.TemplateId.Equals(query, StringComparison.OrdinalIgnoreCase)) return 100;
            if (t.Name.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)) return 50;
            if (t.Description.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)) return 25;
            if (t.Tags.Any(tag => tag.Equals(lowerQuery, StringComparison.OrdinalIgnoreCase))) return 10;
            return 1;
        });
    }

    public async Task<TemplateInstantiationResult> InstantiateTemplateAsync(
        string templateId,
        string agentName,
        Dictionary<string, string>? parameters = null,
        string? parentAgentId = null,
        CancellationToken ct = default)
    {
        var result = new TemplateInstantiationResult();

        try
        {
            // Load template
            var template = await GetTemplateAsync(templateId, ct);
            if (template == null)
            {
                result.Success = false;
                result.Error = $"Template '{templateId}' not found";
                return result;
            }

            _logger.LogInformation("🎭 Instantiating template {TemplateId} as {AgentName}", templateId, agentName);

            // Merge parameters (template defaults + user overrides)
            var mergedParams = new Dictionary<string, string>(template.MergeParameters);
            if (parameters != null)
            {
                foreach (var kvp in parameters)
                {
                    mergedParams[kvp.Key] = kvp.Value;
                }
            }
            mergedParams["agent_name"] = agentName; // Always override agent name

            result.AppliedParameters = mergedParams;

            // Apply parameter substitution to system prompt
            var systemPrompt = ApplyParameterSubstitution(template.SystemPromptTemplate, mergedParams);

            // Create temporary persona from template
            var persona = new Persona
            {
                Name = agentName,
                SystemPrompt = systemPrompt,
                DemonTitle = template.Name,
                Specializations = template.Tags,
                AvailableTools = template.DefaultTools,
                Personality = new PersonalityTraits
                {
                    Tone = template.Category == TemplateCategory.ContentCreation ? "Creative" : "Professional",
                    Approach = template.Category == TemplateCategory.DataAnalysis ? "Analytical" : "Methodical",
                    Verbosity = template.RecommendedRank == AgentRank.Supreme ? 8 : 6,
                    UseDemonicTheme = false // Templates are non-demonic by default
                },
                CustomInstructions = mergedParams.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString() ?? string.Empty)
            };

            // Create agent from factory
            var agent = await _agentFactory.CreateAgentAsync(
                persona,
                template.RecommendedRank,
                parentAgentId,
                personaPath: $"template:{templateId}",
                ct);

            if (agent == null)
            {
                result.Success = false;
                result.Error = "Agent factory failed to create agent";
                return result;
            }

            // Initialize skill tree if provided
            if (template.SkillTree != null)
            {
                await InitializeSkillTreeAsync(agent.Id, template.SkillTree, ct);
            }

            // Increment usage count
            template.UsageCount++;
            await SaveTemplateAsync(template, ct);

            result.Success = true;
            result.AgentId = agent.Id;

            _logger.LogInformation("✅ Template instantiation succeeded: {AgentId}", agent.Id);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Template instantiation failed for {TemplateId}", templateId);
            result.Success = false;
            result.Error = $"Instantiation error: {ex.Message}";
            return result;
        }
    }

    public async Task<bool> RegisterTemplateAsync(AgentTemplate template, CancellationToken ct = default)
    {
        try
        {
            // Validate template
            if (string.IsNullOrWhiteSpace(template.TemplateId))
            {
                _logger.LogWarning("⚠️ Cannot register template without TemplateId");
                return false;
            }

            if (_templateCache.ContainsKey(template.TemplateId))
            {
                _logger.LogWarning("⚠️ Template {TemplateId} already exists", template.TemplateId);
                return false;
            }

            // Save to disk
            await SaveTemplateAsync(template, ct);

            // Add to cache
            _templateCache[template.TemplateId] = template;

            _logger.LogInformation("✅ Registered new template: {TemplateId}", template.TemplateId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to register template {TemplateId}", template.TemplateId);
            return false;
        }
    }

    public async Task<bool> UpdateTemplateAsync(AgentTemplate template, CancellationToken ct = default)
    {
        try
        {
            if (!_templateCache.ContainsKey(template.TemplateId))
            {
                _logger.LogWarning("⚠️ Cannot update non-existent template {TemplateId}", template.TemplateId);
                return false;
            }

            await SaveTemplateAsync(template, ct);
            _templateCache[template.TemplateId] = template;

            _logger.LogInformation("✅ Updated template: {TemplateId}", template.TemplateId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update template {TemplateId}", template.TemplateId);
            return false;
        }
    }

    public async Task<bool> DeleteTemplateAsync(string templateId, CancellationToken ct = default)
    {
        try
        {
            var filePath = Path.Combine(_templatesDirectory, $"{templateId}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            _templateCache.TryRemove(templateId, out _);

            _logger.LogInformation("🗑️ Deleted template: {TemplateId}", templateId);
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete template {TemplateId}", templateId);
            return false;
        }
    }

    public async Task<Dictionary<string, int>> GetTemplateUsageStatsAsync(CancellationToken ct = default)
    {
        await EnsureTemplatesLoadedAsync(ct);
        return _templateCache.Values
            .OrderByDescending(t => t.UsageCount)
            .ToDictionary(t => t.TemplateId, t => t.UsageCount);
    }

    // Private helper methods

    private async Task EnsureTemplatesLoadedAsync(CancellationToken ct)
    {
        if (_templateCache.IsEmpty)
        {
            await _loadLock.WaitAsync(ct);
            try
            {
                if (_templateCache.IsEmpty)
                {
                    await LoadAllTemplatesAsync(ct);
                }
            }
            finally
            {
                _loadLock.Release();
            }
        }
    }

    private async Task LoadAllTemplatesAsync(CancellationToken ct)
    {
        _logger.LogInformation("📂 Loading templates from {Directory}", _templatesDirectory);

        if (!Directory.Exists(_templatesDirectory))
        {
            _logger.LogWarning("⚠️ Templates directory not found: {Directory}", _templatesDirectory);
            return;
        }

        var jsonFiles = Directory.GetFiles(_templatesDirectory, "*.json");
        _logger.LogInformation("📄 Found {Count} template files", jsonFiles.Length);

        foreach (var filePath in jsonFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath, ct);
                var template = JsonSerializer.Deserialize<AgentTemplate>(json, JsonDefaults.WebCaseInsensitive);

                if (template != null && !string.IsNullOrWhiteSpace(template.TemplateId))
                {
                    _templateCache[template.TemplateId] = template;
                    _logger.LogDebug("✅ Loaded template: {TemplateId}", template.TemplateId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to load template from {FilePath}", filePath);
            }
        }

        _logger.LogInformation("✅ Loaded {Count} templates successfully", _templateCache.Count);
    }

    private async Task SaveTemplateAsync(AgentTemplate template, CancellationToken ct)
    {
        var filePath = Path.Combine(_templatesDirectory, $"{template.TemplateId}.json");
        var json = JsonSerializer.Serialize(template, JsonDefaults.WebIndented);

        await File.WriteAllTextAsync(filePath, json, ct);
        _logger.LogDebug("💾 Saved template to {FilePath}", filePath);
    }

    private string ApplyParameterSubstitution(string template, Dictionary<string, string> parameters)
    {
        var result = template;
        foreach (var kvp in parameters)
        {
            var placeholder = $"{{{kvp.Key}}}";
            result = result.Replace(placeholder, kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        // Warn about unsubstituted placeholders
        var unsubstituted = Regex.Matches(result, @"\{(\w+)\}");
        if (unsubstituted.Count > 0)
        {
            _logger.LogWarning("⚠️ Unsubstituted placeholders: {Placeholders}",
                string.Join(", ", unsubstituted.Select(m => m.Value)));
        }

        return result;
    }

    private async Task InitializeSkillTreeAsync(
        string agentId,
        TemplateSkillTree skillTree,
        CancellationToken ct)
    {
        // Award experience points to level up each skill to initial level
        foreach (var skill in skillTree.InitialSkills)
        {
            var targetLevel = skill.Value;
            
            // Award progressively larger XP amounts to reach target level
            // Each level requires more XP than the last
            for (int level = 1; level <= targetLevel; level++)
            {
                // Award enough XP to reach this level (progressively increasing)
                int xpAmount = level * 100; // Simplified: 100 * level XP per level
                await _skillTreeService.AwardExperienceAsync(
                    agentId,
                    skill.Key,
                    success: true,
                    executionTime: TimeSpan.FromMilliseconds(10),
                    complexity: level,
                    ct);
            }
        }

        _logger.LogDebug("🌳 Initialized skill tree for agent {AgentId}: {Skills}",
            agentId, string.Join(", ", skillTree.InitialSkills.Select(s => $"{s.Key}(L{s.Value})")));
    }
}
