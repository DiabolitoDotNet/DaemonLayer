using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Tools.Dynamic;
using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Tools.Tools.Meta;

public sealed class CreateCustomToolTool : ITool
{
    private readonly ILlmClient _llm;
    private readonly IToolRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ICustomToolCompiler _compiler;
    private readonly ICustomToolSecurityPolicy _policy;
    private readonly ICustomToolStore _store;
    private readonly IOptionsMonitor<CustomToolsOptions> _options;
    private readonly ILogger<CreateCustomToolTool> _logger;

    public string Name => "create_custom_tool";
    public string Description => "Generate a new C# tool at runtime from a precise description, compile it, and register it (Supreme-only by default). Persists source to LiteDB for reload on restart.";

    public CreateCustomToolTool(
        ILlmClient llm,
        IToolRegistry registry,
        IServiceProvider services,
        ICustomToolCompiler compiler,
        ICustomToolSecurityPolicy policy,
        ICustomToolStore store,
        IOptionsMonitor<CustomToolsOptions> options,
        ILogger<CreateCustomToolTool> logger)
    {
        _llm = llm;
        _registry = registry;
        _services = services;
        _compiler = compiler;
        _policy = policy;
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (_options.CurrentValue.Enabled != true)
        {
            return new ToolResult { Success = false, Error = "CustomTools are disabled by configuration" };
        }

        var requirement = parameters.GetValueOrDefault("requirement")?.ToString()
            ?? parameters.GetValueOrDefault("description")?.ToString();

        if (string.IsNullOrWhiteSpace(requirement))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Missing required parameter: requirement (or description)"
            };
        }

        var requestedName = parameters.GetValueOrDefault("tool_name")?.ToString();
        var toolName = NormalizeToolName(requestedName, requirement);
        var suffix = toolName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase)
            ? toolName.Substring("custom_".Length)
            : toolName;
        var className = ToPascalCase(suffix);
        if (string.IsNullOrWhiteSpace(className))
        {
            className = "CustomGenerated";
        }
        className = "Custom" + className + "Tool";

        if (_registry.GetTool(toolName) != null)
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Tool '{toolName}' already exists in registry"
            };
        }

        var systemPrompt = BuildSystemPrompt(toolName, className);
        var userPrompt = BuildUserPrompt(requirement);

        string raw;
        try
        {
            raw = await _llm.GetCompletionAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM failed to generate custom tool code");
            return new ToolResult { Success = false, Error = ex.Message };
        }

        var source = ExtractCSharp(raw);
        if (string.IsNullOrWhiteSpace(source))
        {
            return new ToolResult
            {
                Success = false,
                Error = "LLM output did not contain valid C# source code"
            };
        }

        var policyDecision = _policy.Evaluate(source);
        if (!policyDecision.Allowed)
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Custom tool rejected by policy: {policyDecision.Reason}",
                Metadata = new Dictionary<string, object>
                {
                    ["policy_rules"] = policyDecision.MatchedRules.ToArray(),
                    ["tool_name"] = toolName
                }
            };
        }

        var toolId = Guid.NewGuid().ToString("n");
        var hash = Sha256Hex(source);

        var creatorId = parameters.GetValueOrDefault("agent_id")?.ToString() ?? "system";
        var creatorName = parameters.GetValueOrDefault("agent_name")?.ToString() ?? creatorId;

        var definition = new CustomToolDefinition
        {
            Id = toolId,
            ToolName = toolName,
            Description = requirement.Trim(),
            SourceCode = source,
            CreatedByAgentId = creatorId,
            CreatedByAgentName = creatorName,
            CreatedAt = DateTimeOffset.UtcNow,
            RequiresManualApproval = policyDecision.RequiresManualApproval,
            SourceHash = hash
        };

        await _store.UpsertAsync(definition, ct).ConfigureAwait(false);

        var approved = IsApproved(definition, _options.CurrentValue);
        var allowUnsafe = _options.CurrentValue.AllowUnsafeWithoutManualApproval;

        if (definition.RequiresManualApproval && !approved && !allowUnsafe)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Custom tool created but blocked by policy (manual approval required)",
                Output = BuildManualApprovalMessage(definition, policyDecision),
                Metadata = new Dictionary<string, object>
                {
                    ["tool_id"] = definition.Id,
                    ["tool_name"] = definition.ToolName,
                    ["source_hash"] = definition.SourceHash,
                    ["requires_manual_approval"] = true,
                    ["policy_rules"] = policyDecision.MatchedRules.ToArray()
                }
            };
        }

        var compile = await _compiler.CompileAndCreateAsync(source, toolName, _services, _logger, ct).ConfigureAwait(false);
        definition.LastCompiledAt = DateTimeOffset.UtcNow;
        definition.LastCompileError = compile.Success ? null : compile.Error;
        await _store.UpsertAsync(definition, ct).ConfigureAwait(false);

        if (!compile.Success || compile.Tool == null)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Custom tool compilation failed",
                Output = compile.Error ?? string.Empty,
                Metadata = new Dictionary<string, object>
                {
                    ["tool_id"] = definition.Id,
                    ["tool_name"] = definition.ToolName,
                    ["diagnostics"] = compile.Diagnostics.ToArray()
                }
            };
        }

        _registry.RegisterTool(compile.Tool);

        return new ToolResult
        {
            Success = true,
            Output = $"Created and registered tool '{compile.Tool.Name}'. Note: custom tools are Supreme-only by default unless ToolPermissions are configured.",
            Metadata = new Dictionary<string, object>
            {
                ["tool_id"] = definition.Id,
                ["tool_name"] = compile.Tool.Name,
                ["requires_manual_approval"] = definition.RequiresManualApproval,
                ["source_hash"] = definition.SourceHash
            }
        };
    }

    private static string BuildSystemPrompt(string toolName, string className)
    {
        return $"You generate a SINGLE C# file that compiles for .NET (modern C#). " +
               $"It MUST implement InfernalHierarchy.Core.Interfaces.ITool. " +
               $"Output ONLY code (no explanations). " +
               $"Security constraints: do NOT use file IO, networking, process execution, environment access, reflection loading, unsafe code, P/Invoke. " +
               $"Do NOT reference System.IO, System.Net, System.Sockets, System.Diagnostics.Process, System.Environment, System.Reflection, AssemblyLoadContext, DllImport, unsafe. " +
               $"Tool name MUST be exactly '{toolName}'. The class name MUST be exactly '{className}'. " +
               $"The tool should validate parameters defensively and return ToolResult with Success, Output, Error, Metadata.";
    }

    private static string BuildUserPrompt(string requirement)
        => $"Create a tool for this requirement:\n{requirement}\n\n" +
           "Requirements:\n" +
           "- Implement ITool with Name/Description/ExecuteAsync\n" +
           "- Use only safe in-memory operations (string, json, xml parsing, etc.)\n" +
           "- Do not use IO/network/process\n" +
           "- If parameters are missing, return Success=false with Error\n";

    private static string ExtractCSharp(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // Prefer fenced code block
        var fence = Regex.Match(raw, "```(?:csharp|cs)?\\s*(?<code>[\\s\\S]*?)```", RegexOptions.IgnoreCase);
        if (fence.Success)
        {
            return fence.Groups["code"].Value.Trim();
        }

        // Otherwise assume the whole response is code
        return raw.Trim();
    }

    private static string NormalizeToolName(string? requested, string fallback)
    {
        var baseName = !string.IsNullOrWhiteSpace(requested) ? requested! : fallback;
        baseName = baseName.Trim().ToLowerInvariant();

        // slugify
        baseName = Regex.Replace(baseName, "[^a-z0-9]+", "_");
        baseName = baseName.Trim('_');
        if (baseName.Length > 48) baseName = baseName.Substring(0, 48).Trim('_');
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "generated";

        if (!baseName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "custom_" + baseName;
        }

        return baseName;
    }

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (p.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(p[0]));
            if (p.Length > 1) sb.Append(p.Substring(1));
        }
        return sb.ToString();
    }

    private static string Sha256Hex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsApproved(CustomToolDefinition def, CustomToolsOptions options)
    {
        return options.ApprovedToolIds.Any(id => string.Equals(id?.Trim(), def.Id, StringComparison.OrdinalIgnoreCase))
               || options.ApprovedToolNames.Any(n => string.Equals(n?.Trim(), def.ToolName, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildManualApprovalMessage(CustomToolDefinition def, CustomToolPolicyDecision decision)
    {
        var rules = decision.MatchedRules.Count == 0 ? "(none)" : string.Join(", ", decision.MatchedRules);
        return $"Tool '{def.ToolName}' was generated and persisted but NOT registered (policy).\n" +
               $"- tool_id: {def.Id}\n" +
               $"- source_hash: {def.SourceHash}\n" +
               $"- policy_rules: {rules}\n\n" +
               "Manual approval options:\n" +
               "1) Add the tool id to configuration: CustomTools:ApprovedToolIds\n" +
               "2) Or add the tool name to configuration: CustomTools:ApprovedToolNames\n" +
               "Then restart the host (tools reload on startup).";
    }
}
