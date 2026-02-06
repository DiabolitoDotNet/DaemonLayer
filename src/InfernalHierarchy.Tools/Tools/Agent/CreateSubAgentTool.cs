using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace InfernalHierarchy.Tools.Tools.Agent;

/// <summary>
/// Tool for creating sub-agents dynamically
/// </summary>
public class CreateSubAgentTool : ITool
{
    private readonly IAgentFactory _agentFactory;
    private readonly ILogger<CreateSubAgentTool> _logger;

    public string Name => "create_sub_agent";
    public string Description => "Create a new sub-agent with specified persona and rank. Requires: persona_name, rank (Prince/Duke/Worker)";

    public CreateSubAgentTool(IAgentFactory agentFactory, ILogger<CreateSubAgentTool> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!TryGetString(parameters, "persona_name", out var personaName))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: persona_name"
            };
        }

        if (!TryGetString(parameters, "rank", out var rankStr))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: rank"
            };
        }

        if (!Enum.TryParse<AgentRank>(rankStr, true, out var rank))
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Invalid rank: {rankStr}. Must be one of: Supreme, Prince, Duke, Worker"
            };
        }

        var parentId = TryGetString(parameters, "parent_id", out var parsedParentId)
            ? parsedParentId
            : null;

        try
        {
            _logger.LogInformation("🔨 Creating sub-agent: {PersonaName} with rank {Rank}", personaName, rank);

            var agent = await _agentFactory.CreateAgentAsync(personaName, rank, parentId, ct);

            // Start the agent
            await agent.StartAsync(ct);

            return new ToolResult
            {
                Success = true,
                Output = $"Successfully created agent {personaName} (ID: {agent.Id}) with rank {rank}",
                Metadata = new Dictionary<string, object>
                {
                    ["agent_id"] = agent.Id,
                    ["agent_name"] = agent.Name,
                    ["rank"] = rank.ToString()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create sub-agent: {PersonaName}", personaName);
            return new ToolResult
            {
                Success = false,
                Error = $"Failed to create agent: {ex.Message}"
            };
        }
    }

    private static bool TryGetString(Dictionary<string, object> parameters, string key, out string value)
    {
        value = string.Empty;

        if (!parameters.TryGetValue(key, out var obj) || obj is null)
        {
            return false;
        }

        if (obj is string s)
        {
            value = s;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (obj is JsonElement el)
        {
            value = el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? string.Empty,
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => el.GetRawText()
            };

            return !string.IsNullOrWhiteSpace(value);
        }

        value = obj.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
