using System.Text;
using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Tools;

/// <summary>
/// Tool for creating agents from predefined templates
/// </summary>
public class CreateAgentFromTemplateTool : ITool
{
    private readonly ILogger<CreateAgentFromTemplateTool> _logger;
    private readonly ITemplateService _templateService;

    public string Name => "create_agent_from_template";

    public string Description => @"Create a sub-agent from a predefined template with customizable parameters.

Templates provide pre-configured agents for specialized tasks:
- data-analyst-basic: Statistical analysis and insights
- web-researcher-advanced: In-depth web research and synthesis
- code-generator-csharp: C# code generation following best practices
- task-coordinator-standard: Complex task orchestration
- content-writer-technical: Technical documentation and content

**Parameters:**
- template_id (required): The template identifier (e.g., 'data-analyst-basic')
- agent_name (required): Name for the new agent
- parameters (optional): JSON object with template parameters for customization
  Example: {""data_domain"": ""financial"", ""research_topic"": ""AI trends""}

**Examples:**
- Basic: create_agent_from_template(template_id='data-analyst-basic', agent_name='FinanceAnalyst')
- Advanced: create_agent_from_template(template_id='web-researcher-advanced', agent_name='AIResearcher', parameters='{""research_topic"": ""machine learning trends""}')

**Available Templates:**
Use 'list_templates' to see all available templates or search with 'search_templates(query=""keyword"")'.";

    public CreateAgentFromTemplateTool(
        ILogger<CreateAgentFromTemplateTool> logger,
        ITemplateService templateService)
    {
        _logger = logger;
        _templateService = templateService;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        try
        {
            // Extract required parameters
            if (!parameters.TryGetValue("template_id", out var templateIdObj) || templateIdObj is not string templateId)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = "Missing required parameter: template_id"
                };
            }

            if (!parameters.TryGetValue("agent_name", out var agentNameObj) || agentNameObj is not string agentName)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = "Missing required parameter: agent_name"
                };
            }

            // Extract optional template parameters
            Dictionary<string, string>? templateParams = null;
            if (parameters.TryGetValue("parameters", out var paramsObj))
            {
                if (paramsObj is string paramsJson)
                {
                    try
                    {
                        templateParams = JsonSerializer.Deserialize<Dictionary<string, string>>(paramsJson);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse parameters JSON, ignoring");
                    }
                }
                else if (paramsObj is Dictionary<string, string> paramsDict)
                {
                    templateParams = paramsDict;
                }
            }

            // Extract parent agent ID
            parameters.TryGetValue("parent_id", out var parentIdObj);
            var parentId = parentIdObj as string;

            // Instantiate template
            _logger.LogInformation("🎭 Creating sub-agent from template {TemplateId}", templateId);

            var result = await _templateService.InstantiateTemplateAsync(
                templateId,
                agentName,
                templateParams,
                parentId,
                ct);

            if (!result.Success)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = result.Error ?? "Template instantiation failed"
                };
            }

            // Build success response
            var output = new StringBuilder();
            output.AppendLine($"✅ Successfully created agent from template '{templateId}'");
            output.AppendLine($"Agent ID: {result.AgentId}");
            output.AppendLine($"Agent Name: {agentName}");
            if (!string.IsNullOrWhiteSpace(parentId))
            {
                output.AppendLine($"Parent: {parentId}");
            }

            if (result.AppliedParameters.Count > 0)
            {
                output.AppendLine("\nApplied Parameters:");
                foreach (var kvp in result.AppliedParameters)
                {
                    output.AppendLine($"  - {kvp.Key}: {kvp.Value}");
                }
            }

            if (result.Warnings.Count > 0)
            {
                output.AppendLine("\n⚠️ Warnings:");
                foreach (var warning in result.Warnings)
                {
                    output.AppendLine($"  - {warning}");
                }
            }

            output.AppendLine($"\nThe agent is now ready to receive tasks via the message bus.");

            return new ToolResult
            {
                Success = true,
                Output = output.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating agent from template");
            return new ToolResult
            {
                Success = false,
                Error = $"Template instantiation error: {ex.Message}"
            };
        }
    }
}
