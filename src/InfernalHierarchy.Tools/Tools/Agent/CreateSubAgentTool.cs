using System.Globalization;
using System.Text;
using System.Text.Json;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Core.Utilities;

namespace InfernalHierarchy.Tools.Tools.Agent;

/// <summary>
/// Tool for creating sub-agents dynamically
/// </summary>
public class CreateSubAgentTool : ITool
{
    private readonly IAgentFactory _agentFactory;
    private readonly IPersonaLoader _personaLoader;
    private readonly ILogger<CreateSubAgentTool> _logger;

    public string Name => "create_sub_agent";
    public string Description => "Create a new sub-agent. Optional: persona_name (defaults to derived from role/task/description), rank (defaults to Worker). Optional: base_persona (defaults to generic_worker) + role/system_prompt for dynamic specialization.";

    public CreateSubAgentTool(IAgentFactory agentFactory, IPersonaLoader personaLoader, ILogger<CreateSubAgentTool> logger)
    {
        _agentFactory = agentFactory;
        _personaLoader = personaLoader;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var description = TryGetStringAny(parameters, out var parsedDescription, "description", "desc")
            ? parsedDescription
            : null;

        var task = TryGetStringAny(parameters, out var parsedTask, "task", "objective")
            ? parsedTask
            : null;

        var requestedPersonaName = TryGetStringAny(parameters, out var parsedPersonaName, "persona_name", "personaName", "persona")
            ? parsedPersonaName
            : null;

        var parentId = TryGetStringAny(parameters, out var parsedParentId, "parent_id", "parent_agent_id", "parentId")
            ? parsedParentId
            : null;

        var basePersonaName = TryGetStringAny(parameters, out var parsedBasePersona, "base_persona", "basePersona", "base_soul", "baseSoul", "soul", "template_persona")
            ? parsedBasePersona
            : "generic_worker";

        var role = TryGetStringAny(parameters, out var parsedRole, "role", "mission", "specialization", "specialisation", "job")
            ? parsedRole
            : null;

        var userLocation = TryGetStringAny(parameters, out var parsedUserLocation, "user_location", "userLocation", "location")
            ? parsedUserLocation
            : null;

        var systemPromptOverride = TryGetStringAny(parameters, out var parsedSystemPrompt, "system_prompt", "systemPrompt", "prompt", "instruction", "instructions")
            ? parsedSystemPrompt
            : null;

        // rank is optional; default to Worker for safety
        var rank = AgentRank.Worker;
        if (TryGetStringAny(parameters, out var rankStr, "rank", "agent_rank", "agentRank"))
        {
            if (!Enum.TryParse<AgentRank>(rankStr, true, out rank))
            {
                return new ToolResult
                {
                    Success = false,
                    Error = $"Invalid rank: {rankStr}. Must be one of: Supreme, Prince, Duke, Worker"
                };
            }
        }

        // persona_name is optional; derive a stable safe name
        var personaSeed = !string.IsNullOrWhiteSpace(requestedPersonaName)
            ? requestedPersonaName!
            : !string.IsNullOrWhiteSpace(role)
                ? role!
                : !string.IsNullOrWhiteSpace(task)
                    ? task!
                    : !string.IsNullOrWhiteSpace(description)
                        ? description!
                        : "specialist";

        var personaName = NormalizeSafeName(personaSeed);

        try
        {
            _logger.LogInformation(
                "🔨 Creating sub-agent: RequestedPersona={RequestedPersona} RuntimePersona={PersonaName} Rank={Rank}",
                requestedPersonaName ?? string.Empty,
                personaName,
                rank);

            IAgent agent;
            var loadedPersona = await _personaLoader.LoadPersonaAsync(personaName, ct);
            if (loadedPersona is not null)
            {
                agent = await _agentFactory.CreateAgentAsync(personaName, rank, parentId, ct);
            }
            else
            {
                agent = await CreateDerivedAgentAsync(
                    personaName,
                    rank,
                    parentId,
                    basePersonaName,
                    role,
                    task,
                    description,
                    userLocation,
                    systemPromptOverride,
                    ct);
            }

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
                    ,
                    ["requested_persona_name"] = requestedPersonaName ?? string.Empty,
                    ["runtime_persona_name"] = personaName
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

    private static bool IsPersonaNotFound(InvalidOperationException ex)
        => ex.Message.Contains("Persona", StringComparison.OrdinalIgnoreCase) &&
           ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private async Task<IAgent> CreateDerivedAgentAsync(
        string derivedPersonaName,
        AgentRank rank,
        string? parentId,
        string basePersonaName,
        string? role,
        string? task,
        string? description,
        string? userLocation,
        string? systemPromptOverride,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Persona '{PersonaName}' not found; falling back to base persona '{BasePersonaName}' with dynamic overrides",
            derivedPersonaName,
            basePersonaName);

        var basePersona = await _personaLoader.LoadPersonaAsync(basePersonaName, ct);
        if (basePersona == null)
        {
            throw new InvalidOperationException($"Base persona '{basePersonaName}' not found");
        }

        var derivedSpecializations = basePersona.Specializations?.ToList() ?? new List<string>();
        if (!string.IsNullOrWhiteSpace(role) && !derivedSpecializations.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            derivedSpecializations.Add(role);
        }

        var derivedSystemPrompt = !string.IsNullOrWhiteSpace(systemPromptOverride)
            ? systemPromptOverride
            : BuildDerivedSystemPrompt(basePersona.SystemPrompt, role, task, description, userLocation);

        var derived = new Persona
        {
            Name = derivedPersonaName,
            DemonTitle = !string.IsNullOrWhiteSpace(role) ? role : basePersona.DemonTitle,
            SystemPrompt = derivedSystemPrompt,
            ModelOverride = basePersona.ModelOverride,
            Personality = basePersona.Personality,
            Specializations = derivedSpecializations,
            AvailableTools = basePersona.AvailableTools?.ToList() ?? new List<string>(),
            CustomInstructions = basePersona.CustomInstructions != null
                ? new Dictionary<string, string>(basePersona.CustomInstructions)
                : new Dictionary<string, string>()
        };

        var personaPath = $"souls/{KeyNormalization.NormalizePersonaKey(basePersonaName)}.json";
        return await _agentFactory.CreateAgentAsync(derived, rank, parentId, personaPath, ct);
    }

    private static string BuildDerivedSystemPrompt(
        string? basePrompt,
        string? role,
        string? task,
        string? description,
        string? userLocation)
    {
        var sb = new StringBuilder((basePrompt ?? string.Empty).TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Dynamic assignment (strict):");

        if (!string.IsNullOrWhiteSpace(role))
        {
            sb.AppendLine($"- Active role: {role}");
        }

        if (!string.IsNullOrWhiteSpace(task))
        {
            sb.AppendLine($"- Task: {task}");
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine($"- Description: {description}");
        }

        if (!string.IsNullOrWhiteSpace(userLocation))
        {
            sb.AppendLine($"- User location/context: {userLocation}");
        }

        sb.AppendLine("- Focus on this assignment unless explicitly told otherwise.");
        return sb.ToString();
    }

    private static string NormalizeSafeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "specialist";
        }

        // Remove diacritics (e.g., "Météo" -> "Meteo")
        var formD = raw.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        var noDiacritics = sb.ToString().Normalize(NormalizationForm.FormC);

        // Keep only safe characters: [A-Za-z0-9_-]
        var safe = new StringBuilder(noDiacritics.Length);
        foreach (var c in noDiacritics)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
            {
                safe.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                safe.Append('_');
            }
        }

        var normalized = safe.ToString().Trim('_', '-');
        if (normalized.Length > 50)
        {
            normalized = normalized[..50];
        }

        return string.IsNullOrWhiteSpace(normalized) ? "specialist" : normalized;
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

    private static bool TryGetStringAny(Dictionary<string, object> parameters, out string value, params string[] keys)
    {
        value = string.Empty;

        foreach (var key in keys)
        {
            if (TryGetString(parameters, key, out var parsed) && !string.IsNullOrWhiteSpace(parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }
}
