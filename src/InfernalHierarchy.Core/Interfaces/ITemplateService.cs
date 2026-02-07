
namespace InfernalHierarchy.Core.Interfaces;

/// <summary>
/// Service for managing agent templates
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// Load a template by its unique ID
    /// </summary>
    Task<AgentTemplate?> GetTemplateAsync(string templateId, CancellationToken ct = default);

    /// <summary>
    /// Get all available templates
    /// </summary>
    Task<IEnumerable<AgentTemplate>> GetAllTemplatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get templates by category
    /// </summary>
    Task<IEnumerable<AgentTemplate>> GetTemplatesByCategoryAsync(
        TemplateCategory category,
        CancellationToken ct = default);

    /// <summary>
    /// Search templates by tags or keywords
    /// </summary>
    Task<IEnumerable<AgentTemplate>> SearchTemplatesAsync(
        string query,
        CancellationToken ct = default);

    /// <summary>
    /// Create an agent from a template with parameter substitution
    /// </summary>
    Task<TemplateInstantiationResult> InstantiateTemplateAsync(
        string templateId,
        string agentName,
        Dictionary<string, string>? parameters = null,
        string? parentAgentId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Register a custom template (for user-defined templates)
    /// </summary>
    Task<bool> RegisterTemplateAsync(
        AgentTemplate template,
        CancellationToken ct = default);

    /// <summary>
    /// Update an existing template
    /// </summary>
    Task<bool> UpdateTemplateAsync(
        AgentTemplate template,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a template by ID
    /// </summary>
    Task<bool> DeleteTemplateAsync(
        string templateId,
        CancellationToken ct = default);

    /// <summary>
    /// Get template usage statistics
    /// </summary>
    Task<Dictionary<string, int>> GetTemplateUsageStatsAsync(
        CancellationToken ct = default);
}
