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
    private readonly ICapabilityOutcomePublisher? _outcomePublisher;
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
        ILogger<CreateCustomToolTool> logger,
        ICapabilityOutcomePublisher? outcomePublisher = null)
    {
        _llm = llm;
        _registry = registry;
        _services = services;
        _compiler = compiler;
        _policy = policy;
        _store = store;
        _outcomePublisher = outcomePublisher;
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

        var overwrite = GetBool(parameters, "overwrite")
                        || GetBool(parameters, "force")
                        || GetBool(parameters, "overwrite_existing")
                        || GetBool(parameters, "overwriteExisting");

        if (_registry.GetTool(toolName) != null && !overwrite)
        {
            return new ToolResult
            {
                Success = true,
                Output = $"Tool '{toolName}' already exists in registry (idempotent create)",
                Metadata = new Dictionary<string, object>
                {
                    ["tool_name"] = toolName,
                    ["already_exists"] = true
                }
            };
        }

        CustomToolDefinition? existingDefinition = null;
        if (overwrite)
        {
            try
            {
                existingDefinition = await _store.GetByNameAsync(toolName, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load existing custom tool definition for overwrite: {ToolName}", toolName);
            }
        }

        var requestedTemplate = parameters.GetValueOrDefault("template")?.ToString();
        var usedTemplate = false;
        string source;

        if (ShouldUseHttpGetJsonTemplate(requestedTemplate, toolName, requirement))
        {
            usedTemplate = true;
            source = BuildHttpGetJsonToolSource(toolName, className);
        }
        else
        {
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

            source = ExtractCSharp(raw);
            if (string.IsNullOrWhiteSpace(source))
            {
                return new ToolResult
                {
                    Success = false,
                    Error = "LLM output did not contain valid C# source code"
                };
            }
        }

        return await CompilePersistAndRegisterAsync(
            parameters,
            requirement,
            toolName,
            source,
            usedTemplate,
            overwrite,
            existingDefinition,
            ct).ConfigureAwait(false);
    }

    private async Task<ToolResult> CompilePersistAndRegisterAsync(
        Dictionary<string, object> parameters,
        string requirement,
        string toolName,
        string source,
        bool usedTemplate,
        bool overwrite,
        CustomToolDefinition? existingDefinition,
        CancellationToken ct)
    {
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
                    ["tool_name"] = toolName,
                    ["used_template"] = usedTemplate
                }
            };
        }

        var toolId = existingDefinition?.Id ?? Guid.NewGuid().ToString("n");
        var hash = Sha256Hex(source);

        var creatorId = parameters.GetValueOrDefault("agent_id")?.ToString() ?? "system";
        var creatorName = parameters.GetValueOrDefault("agent_name")?.ToString() ?? creatorId;

        var effectiveRequiresManualApproval = policyDecision.RequiresManualApproval
            && !IsNetworkOnly(policyDecision.MatchedRules, _options.CurrentValue);

        var definition = new CustomToolDefinition
        {
            Id = toolId,
            ToolName = toolName,
            Description = requirement.Trim(),
            SourceCode = source,
            CreatedByAgentId = existingDefinition?.CreatedByAgentId ?? creatorId,
            CreatedByAgentName = existingDefinition?.CreatedByAgentName ?? creatorName,
            CreatedAt = existingDefinition?.CreatedAt ?? DateTimeOffset.UtcNow,
            RequiresManualApproval = effectiveRequiresManualApproval,
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
                    ["policy_rules"] = policyDecision.MatchedRules.ToArray(),
                    ["used_template"] = usedTemplate
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
                    ["diagnostics"] = compile.Diagnostics.ToArray(),
                    ["used_template"] = usedTemplate
                }
            };
        }

        _registry.RegisterTool(compile.Tool);

        if (_outcomePublisher is not null)
        {
            await _outcomePublisher.RecordOutcomeAsync(new CapabilityOutcome
            {
                Kind = CapabilityOutcomeKind.CustomToolCreated,
                CapabilityId = definition.ToolName,
                CapabilityType = "custom_tool",
                SourceTask = requirement,
                RiskLevel = definition.RequiresManualApproval ? "High" : "Low",
                AgentId = creatorId,
                OccurredAtUtc = DateTime.UtcNow
            }, ct).ConfigureAwait(false);
        }

        return new ToolResult
        {
            Success = true,
            Output = overwrite
                ? $"Overwrote and registered tool '{compile.Tool.Name}'. Note: custom tools are Supreme-only by default unless ToolPermissions are configured."
                : $"Created and registered tool '{compile.Tool.Name}'. Note: custom tools are Supreme-only by default unless ToolPermissions are configured.",
            Metadata = new Dictionary<string, object>
            {
                ["tool_id"] = definition.Id,
                ["tool_name"] = compile.Tool.Name,
                ["requires_manual_approval"] = definition.RequiresManualApproval,
                ["source_hash"] = definition.SourceHash,
                ["used_template"] = usedTemplate,
                ["overwrote_existing"] = overwrite
            }
        };
    }

    private static bool GetBool(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var v) || v is null) return false;
        if (v is bool b) return b;
        return bool.TryParse(v.ToString(), out var parsed) && parsed;
    }

    private static bool ShouldUseHttpGetJsonTemplate(string? requestedTemplate, string toolName, string requirement)
    {
        if (!string.IsNullOrWhiteSpace(requestedTemplate)
            && string.Equals(requestedTemplate.Trim(), "http_get_json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(toolName, "custom_http_get_json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "custom_lacale_api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var r = requirement ?? string.Empty;
        return r.Contains("httpclient", StringComparison.OrdinalIgnoreCase)
               && r.Contains("get", StringComparison.OrdinalIgnoreCase)
               && r.Contains("endpoint", StringComparison.OrdinalIgnoreCase)
               && (r.Contains("base_url", StringComparison.OrdinalIgnoreCase)
                   || r.Contains("base url", StringComparison.OrdinalIgnoreCase))
               && r.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHttpGetJsonToolSource(string toolName, string className)
    {
        return $$"""
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;

namespace InfernalHierarchy.CustomTools;

public sealed class {{className}} : ITool
{
    public string Name => "{{toolName}}";
    public string Description => "HTTP GET JSON (raw) via HttpClient: base_url + endpoint + query_params";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (parameters is null)
        {
            return new ToolResult { Success = false, Error = "Missing parameters" };
        }

        var baseUrl = GetString(parameters, "base_url") ?? GetString(parameters, "baseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new ToolResult { Success = false, Error = "Missing required parameter: base_url" };
        }

        var endpoint = GetString(parameters, "endpoint") ?? string.Empty;
        var apiKey = GetString(parameters, "api_key") ?? GetString(parameters, "apiKey");
        var apiKeyHeader = GetString(parameters, "api_key_header") ?? GetString(parameters, "apiKeyHeader") ?? "Authorization";
        var bearer = GetBool(parameters, "bearer", defaultValue: true);
        var query = TryGetQueryParams(parameters);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return new ToolResult { Success = false, Error = "Invalid base_url (must be absolute URI)", Output = baseUrl };
        }

        var full = Combine(baseUri, endpoint);
        if (query.Count > 0)
        {
            var ub = new UriBuilder(full);
            ub.Query = BuildQueryString(query);
            full = ub.Uri;
        }

        using var http = new HttpClient();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (string.Equals(apiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase) && bearer)
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            else
            {
                http.DefaultRequestHeaders.Remove(apiKeyHeader);
                http.DefaultRequestHeaders.Add(apiKeyHeader, apiKey);
            }
        }

        HttpResponseMessage resp;
        try
        {
            resp = await http.GetAsync(full, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = "HTTP request failed", Output = $"{ex.Message}\nURL: {full}" };
        }

        string body;
        try
        {
            body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = "Failed to read HTTP response", Output = ex.Message };
        }

        var meta = new Dictionary<string, object>
        {
            ["url"] = full.ToString(),
            ["status_code"] = (int)resp.StatusCode
        };

        if (!resp.IsSuccessStatusCode)
        {
            return new ToolResult { Success = false, Error = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", Output = body, Metadata = meta };
        }

        return new ToolResult { Success = true, Output = body, Metadata = meta };
    }

    private static string? GetString(Dictionary<string, object> p, string key)
    {
        if (!p.TryGetValue(key, out var v) || v is null) return null;
        return v.ToString();
    }

    private static bool GetBool(Dictionary<string, object> p, string key, bool defaultValue)
    {
        if (!p.TryGetValue(key, out var v) || v is null) return defaultValue;
        if (v is bool b) return b;
        if (bool.TryParse(v.ToString(), out var parsed)) return parsed;
        return defaultValue;
    }

    private static Dictionary<string, string> TryGetQueryParams(Dictionary<string, object> p)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!p.TryGetValue("query_params", out var v) || v is null) return result;

        if (v is Dictionary<string, object> dictObj)
        {
            foreach (var kv in dictObj)
            {
                if (kv.Value is null) continue;
                result[kv.Key] = kv.Value.ToString() ?? string.Empty;
            }
            return result;
        }

        if (v is Dictionary<string, string> dictStr)
        {
            foreach (var kv in dictStr)
            {
                if (kv.Value is null) continue;
                result[kv.Key] = kv.Value;
            }
            return result;
        }

        var s = v.ToString();
        if (string.IsNullOrWhiteSpace(s)) return result;
        foreach (var part in s.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var k = Uri.UnescapeDataString(part.Substring(0, idx));
            var val = Uri.UnescapeDataString(part.Substring(idx + 1));
            if (!string.IsNullOrWhiteSpace(k)) result[k] = val;
        }

        return result;
    }

    private static Uri Combine(Uri baseUri, string endpoint)
    {
        endpoint = endpoint?.Trim() ?? string.Empty;
        if (endpoint.Length == 0) return baseUri;

        // IMPORTANT: strings like "/get" are treated as absolute file URIs (file:///get).
        // Only treat endpoint as absolute when it is a real HTTP/HTTPS URL.
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var abs)
            && (string.Equals(abs.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(abs.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return abs;
        }

        if (!baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) && !endpoint.StartsWith("/", StringComparison.Ordinal))
        {
            endpoint = "/" + endpoint;
        }
        return new Uri(baseUri, endpoint);
    }

    private static string BuildQueryString(Dictionary<string, string> query)
    {
        var sb = new StringBuilder();
        foreach (var kv in query)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value ?? string.Empty));
        }
        return sb.ToString();
    }
}
""";
    }

    private static string BuildSystemPrompt(string toolName, string className)
    {
        return $"You generate a SINGLE C# file that compiles for .NET (modern C#). " +
               $"It MUST implement InfernalHierarchy.Core.Interfaces.ITool. " +
               $"Output ONLY code (no explanations). " +
               $"Security constraints: do NOT use file IO, process execution, environment access, reflection loading, unsafe code, P/Invoke. " +
               $"Networking is allowed ONLY via HttpClient to call HTTPS APIs when required by the tool description. " +
               $"Do NOT reference System.IO, System.Diagnostics.Process, System.Environment, System.Reflection, AssemblyLoadContext, DllImport, unsafe. " +
               $"Tool name MUST be exactly '{toolName}'. The class name MUST be exactly '{className}'. " +
               $"The tool should validate parameters defensively and return ToolResult with Success, Output, Error, Metadata.";
    }

    private static string BuildUserPrompt(string requirement)
        => $"Create a tool for this requirement:\n{requirement}\n\n" +
           "Requirements:\n" +
           "- Implement ITool with Name/Description/ExecuteAsync\n" +
           "- Use only safe in-memory operations (string, json, xml parsing, etc.)\n" +
           "- Do not use IO/process\n" +
           "- If you must call an external API, use HttpClient (HTTPS only)\n" +
           "- If parameters are missing, return Success=false with Error\n";

    private static string ExtractCSharp(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var fence = Regex.Match(raw, "```(?:csharp|cs)?\\s*(?<code>[\\s\\S]*?)```", RegexOptions.IgnoreCase);
        if (fence.Success)
        {
            return fence.Groups["code"].Value.Trim();
        }

        return raw.Trim();
    }

    private static string NormalizeToolName(string? requested, string fallback)
    {
        var baseName = !string.IsNullOrWhiteSpace(requested) ? requested! : fallback;
        baseName = baseName.Trim().ToLowerInvariant();

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
               "3) Or set CustomTools:AllowUnsafeWithoutManualApproval=true to bypass approvals (broad).\n" +
               "Then restart the host (tools reload on startup).";
    }

    private static bool IsNetworkOnly(IReadOnlyList<string> matchedRules, CustomToolsOptions options)
    {
        if (!options.AllowNetworkWithoutManualApproval)
        {
            return false;
        }

        if (matchedRules is null || matchedRules.Count == 0)
        {
            return false;
        }

        foreach (var rule in matchedRules)
        {
            if (string.Equals(rule, "Network namespaces", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(rule, "HttpClient/WebRequest", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
