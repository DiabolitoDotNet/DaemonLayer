using System.Text;

namespace InfernalHierarchy.Tools.Tools.Templates;

/// <summary>
/// Tool for listing available agent templates
/// </summary>
public class ListTemplatesTool : ITool
{
    private readonly ILogger<ListTemplatesTool> _logger;
    private readonly ITemplateService _templateService;

    public string Name => "list_templates";

    public string Description => @"List all available agent templates with their categories and descriptions.

**Parameters:**
- category (optional): Filter by template category (DataAnalysis, Research, Development, Coordination, ContentCreation, etc.)

**Examples:**
- list_templates()  # List all templates
- list_templates(category='DataAnalysis')  # Only data analysis templates";

    public ListTemplatesTool(
        ILogger<ListTemplatesTool> logger,
        ITemplateService templateService)
    {
        _logger = logger;
        _templateService = templateService;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        try
        {
            IEnumerable<AgentTemplate> templates;

            // Extract optional category filter
            if (parameters.TryGetValue("category", out var categoryObj) &&
                categoryObj is string categoryStr &&
                Enum.TryParse<TemplateCategory>(categoryStr, true, out var category))
            {
                templates = await _templateService.GetTemplatesByCategoryAsync(category, ct);
                _logger.LogDebug("📋 Listing templates for category: {Category}", category);
            }
            else
            {
                templates = await _templateService.GetAllTemplatesAsync(ct);
                _logger.LogDebug("📋 Listing all templates");
            }

            var output = new StringBuilder();
            output.AppendLine("📚 Available Agent Templates:");
            output.AppendLine();

            var groupedByCategory = templates.GroupBy(t => t.Category);
            foreach (var group in groupedByCategory.OrderBy(g => g.Key))
            {
                output.AppendLine($"## {group.Key}");
                output.AppendLine();

                foreach (var template in group.OrderBy(t => t.Name))
                {
                    output.AppendLine($"### {template.Name}");
                    output.AppendLine($"**ID:** `{template.TemplateId}`");
                    output.AppendLine($"**Rank:** {template.RecommendedRank}");
                    output.AppendLine($"**Description:** {template.Description}");
                    output.AppendLine($"**Tools:** {string.Join(", ", template.DefaultTools)}");
                    output.AppendLine($"**Tags:** {string.Join(", ", template.Tags)}");
                    output.AppendLine($"**Usage Count:** {template.UsageCount}");
                    output.AppendLine();
                }
            }

            output.AppendLine($"**Total Templates:** {templates.Count()}");

            return new ToolResult
            {
                Success = true,
                Output = output.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing templates");
            return new ToolResult
            {
                Success = false,
                Error = $"Failed to list templates: {ex.Message}"
            };
        }
    }
}
