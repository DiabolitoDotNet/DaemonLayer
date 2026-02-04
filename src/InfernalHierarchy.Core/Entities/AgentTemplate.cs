namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Template for creating specialized agents with predefined configurations
/// </summary>
public class AgentTemplate
{
    /// <summary>
    /// Unique template identifier (e.g., "data-analyst-v1", "web-scraper-basic")
    /// </summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable template name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Template category for organization
    /// </summary>
    public TemplateCategory Category { get; set; }

    /// <summary>
    /// Detailed description of what this template is for
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Recommended rank for agents created from this template
    /// </summary>
    public AgentRank RecommendedRank { get; set; }

    /// <summary>
    /// Base system prompt that defines the agent's behavior
    /// </summary>
    public string SystemPromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Default tools available to agents created from this template
    /// </summary>
    public List<string> DefaultTools { get; set; } = new();

    /// <summary>
    /// Predefined skill tree configuration
    /// </summary>
    public TemplateSkillTree? SkillTree { get; set; }

    /// <summary>
    /// Collaboration preferences (strategies this template excels at)
    /// </summary>
    public List<string> PreferredCollaborationStrategies { get; set; } = new();

    /// <summary>
    /// Tags for searching/filtering templates
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Version of this template (semantic versioning)
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Template author/creator
    /// </summary>
    public string Author { get; set; } = "System";

    /// <summary>
    /// When this template was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Template usage count for popularity tracking
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// Merge parameters for customization (e.g., {domain}, {language}, {max_tokens})
    /// </summary>
    public Dictionary<string, string> MergeParameters { get; set; } = new();
}

/// <summary>
/// Template categories for organizing agent templates
/// </summary>
public enum TemplateCategory
{
    /// <summary>
    /// General-purpose agents without specialization
    /// </summary>
    General,

    /// <summary>
    /// Data analysis, statistics, insights
    /// </summary>
    DataAnalysis,

    /// <summary>
    /// Software development, code generation, debugging
    /// </summary>
    Development,

    /// <summary>
    /// Information gathering, web search, summarization
    /// </summary>
    Research,

    /// <summary>
    /// Task coordination, workflow management
    /// </summary>
    Coordination,

    /// <summary>
    /// Content creation, writing, editing
    /// </summary>
    ContentCreation,

    /// <summary>
    /// Communication, messaging, notifications
    /// </summary>
    Communication,

    /// <summary>
    /// System monitoring, health checks, diagnostics
    /// </summary>
    Monitoring,

    /// <summary>
    /// Testing, quality assurance, validation
    /// </summary>
    Testing,

    /// <summary>
    /// Security, authorization, threat detection
    /// </summary>
    Security,

    /// <summary>
    /// Custom templates created by users
    /// </summary>
    Custom
}

/// <summary>
/// Skill tree configuration for templates
/// </summary>
public class TemplateSkillTree
{
    /// <summary>
    /// Skill nodes with initial levels
    /// </summary>
    public Dictionary<string, int> InitialSkills { get; set; } = new();

    /// <summary>
    /// Maximum skill level for this template
    /// </summary>
    public int MaxSkillLevel { get; set; } = 10;

    /// <summary>
    /// Experience multiplier for faster/slower learning
    /// </summary>
    public double ExperienceMultiplier { get; set; } = 1.0;
}

/// <summary>
/// Result of template instantiation
/// </summary>
public class TemplateInstantiationResult
{
    /// <summary>
    /// Whether instantiation succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The created agent ID (if successful)
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Error message (if failed)
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Warnings during instantiation (non-fatal issues)
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Applied merge parameters
    /// </summary>
    public Dictionary<string, string> AppliedParameters { get; set; } = new();
}
