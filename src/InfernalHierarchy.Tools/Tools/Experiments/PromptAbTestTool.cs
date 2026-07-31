using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfernalHierarchy.Tools.Tools.Experiments;

/// <summary>
/// Runs A/B (or A/B/C/...) prompt experiments by executing the same task against multiple system prompts.
/// Designed for local-first prompt optimization and regression checks.
/// </summary>
public class PromptAbTestTool : ITool
{
    private static readonly JsonSerializerOptions _jsonOptions = JsonDefaults.WebIndented;

    private static double ScoreResponse(string response, ScoringCriteria criteria)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return 0;
        }

        var components = new List<double>();

        if (criteria.MustBeJson)
        {
            components.Add(IsValidJson(response) ? 1 : 0);
        }

        if (criteria.ExpectedContains.Count > 0)
        {
            var hits = criteria.ExpectedContains.Count(term =>
                !string.IsNullOrWhiteSpace(term) &&
                response.Contains(term, StringComparison.OrdinalIgnoreCase));
            components.Add((double)hits / criteria.ExpectedContains.Count);
        }

        if (!string.IsNullOrWhiteSpace(criteria.ExpectedRegex))
        {
            try
            {
                components.Add(Regex.IsMatch(
                        response,
                        criteria.ExpectedRegex,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    ? 1
                    : 0);
            }
            catch
            {
                // Bad regex should not crash the tool; treat as unmet.
                components.Add(0);
            }
        }

        if (components.Count == 0)
        {
            // Baseline heuristic: non-empty responses get a small positive score.
            return 0.25;
        }

        return components.Average();
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (text.Length <= maxChars)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, maxChars), "...");
    }

    private static bool TryGetString(Dictionary<string, object> parameters, string key, out string value)
    {
        if (!parameters.TryGetValue(key, out var obj))
        {
            value = string.Empty;
            return false;
        }

        switch (obj)
        {
            case string s:
                value = s;
                return true;
            case JsonElement el when el.ValueKind == JsonValueKind.String:
                value = el.GetString() ?? string.Empty;
                return true;
            default:
                value = obj?.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
        }
    }

    private static bool TryGetInt(Dictionary<string, object> parameters, string key, out int value)
    {
        value = 0;
        if (!parameters.TryGetValue(key, out var obj) || obj == null)
        {
            return false;
        }

        if (obj is int i)
        {
            value = i;
            return true;
        }

        if (obj is long l)
        {
            value = (int)Math.Clamp(l, int.MinValue, int.MaxValue);
            return true;
        }

        if (obj is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsed))
            {
                value = parsed;
                return true;
            }

            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out parsed))
            {
                value = parsed;
                return true;
            }
        }

        return int.TryParse(obj.ToString(), out value);
    }

    private static bool TryGetBool(Dictionary<string, object> parameters, string key, out bool value)
    {
        value = false;
        if (!parameters.TryGetValue(key, out var obj) || obj == null)
        {
            return false;
        }

        if (obj is bool b)
        {
            value = b;
            return true;
        }

        if (obj is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            if (el.ValueKind == JsonValueKind.False)
            {
                value = false;
                return true;
            }

            if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        return bool.TryParse(obj.ToString(), out value);
    }

    private static IReadOnlyList<string> TryGetStringArray(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var obj) || obj == null)
        {
            return Array.Empty<string>();
        }

        if (obj is string s)
        {
            // Allow comma-separated values.
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length == 0 ? Array.Empty<string>() : parts;
        }

        if (obj is JsonElement el && el.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        list.Add(val);
                    }
                }
            }
            return list;
        }

        if (obj is IEnumerable<object> items)
        {
            return items.Select(x => x?.ToString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private readonly ILlmClient _llm;
    private readonly IPersonaLoader _personaLoader;
    private readonly ILogger<PromptAbTestTool> _logger;

    public string Name => "prompt_ab_test";

    public string Description =>
        "Run an A/B test across multiple system prompts against the same task and compare results. " +
        "Parameters: task (required), trials (optional, default 3), variants (optional array) or variants_json (optional string JSON). " +
        "Variant fields: name (required), system_prompt (optional), persona (optional), prepend (optional), append (optional). " +
        "Criteria: must_be_json (optional bool), expected_contains (optional array of strings), expected_regex (optional string).";

    public PromptAbTestTool(ILlmClient llm, IPersonaLoader personaLoader, ILogger<PromptAbTestTool> logger)
    {
        _llm = llm;
        _personaLoader = personaLoader;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!TryGetStringAny(parameters, out var task, "task", "prompt", "instruction") || string.IsNullOrWhiteSpace(task))
        {
            return new ToolResult { Success = false, Error = "Missing required parameter: task" };
        }

        var trials = TryGetInt(parameters, "trials", out var t) ? Math.Clamp(t, 1, 50) : 3;

        var mustBeJson = TryGetBool(parameters, "must_be_json", out var mbj) && mbj;
        var expectedRegex = TryGetString(parameters, "expected_regex", out var rex) ? rex : null;
        var expectedContains = TryGetStringArray(parameters, "expected_contains");

        var (variants, variantsError) = await ParseVariantsAsync(parameters, ct);
        if (!string.IsNullOrWhiteSpace(variantsError))
        {
            _logger.LogWarning("Failed parsing variants: {Error}", variantsError);
            return new ToolResult { Success = false, Error = $"Invalid variants: {variantsError}" };
        }

        if (variants.Count < 2)
        {
            return new ToolResult
            {
                Success = false,
                Error = "At least 2 variants are required (provide variants or variants_json)."
            };
        }

        var criteria = new ScoringCriteria(mustBeJson, expectedContains, expectedRegex);

        var results = new List<VariantResult>(variants.Count);
        foreach (var variant in variants)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await RunVariantAsync(variant, task, trials, criteria, ct));
        }

        var winner = results
            .OrderByDescending(r => r.AverageScore)
            .ThenByDescending(r => r.SuccessfulTrials)
            .First();

        var payload = new AbTestReport
        {
            Task = task,
            Trials = trials,
            Criteria = criteria,
            Results = results,
            Winner = new WinnerSummary(winner.Name, winner.AverageScore)
        };

        return new ToolResult
        {
            Success = true,
            Output = JsonSerializer.Serialize(payload, _jsonOptions),
            Metadata = new Dictionary<string, object>
            {
                ["winner"] = winner.Name,
                ["variants"] = variants.Count,
                ["trials"] = trials
            }
        };
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

    private async Task<VariantResult> RunVariantAsync(
        PromptVariant variant,
        string task,
        int trials,
        ScoringCriteria criteria,
        CancellationToken ct)
    {
        var scores = new List<double>(trials);
        var samples = new List<string>(Math.Min(trials, 3));
        var durationsMs = new List<long>(trials);
        var errors = new List<string>();

        for (var i = 0; i < trials; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                var response = await _llm.GetCompletionAsync(variant.SystemPrompt, task, ct);
                sw.Stop();

                var score = ScoreResponse(response ?? string.Empty, criteria);
                scores.Add(score);
                durationsMs.Add((long)sw.Elapsed.TotalMilliseconds);

                if (samples.Count < 3)
                {
                    samples.Add(Truncate(response ?? string.Empty, 700));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                errors.Add(ex.Message);
                durationsMs.Add((long)sw.Elapsed.TotalMilliseconds);
                scores.Add(0);
            }
        }

        var avg = scores.Count == 0 ? 0 : scores.Average();

        return new VariantResult
        {
            Name = variant.Name,
            AverageScore = Math.Round(avg, 4),
            SuccessfulTrials = scores.Count(s => s > 0),
            Scores = scores,
            Samples = samples,
            DurationsMs = durationsMs,
            Errors = errors
        };
    }

    private async Task<(List<PromptVariant> Variants, string? Error)> ParseVariantsAsync(
        Dictionary<string, object> parameters,
        CancellationToken ct)
    {
        // Prefer variants_json for easy manual invocation.
        if (TryGetString(parameters, "variants_json", out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var parsed = new List<VariantInput>();
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        parsed.Add(VariantInput.FromJsonElement(item));
                    }

                    return await NormalizeVariantsAsync(parsed, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException ex)
            {
                // Fall back to Deserialize for callers who already match our CLR property names.
                _logger.LogDebug(ex, "Failed to parse variants_json as JsonDocument; falling back to deserialization");
            }
            catch (Exception ex)
            {
                return (new List<PromptVariant>(), ex.Message);
            }

            try
            {
                var parsedFallback = JsonSerializer.Deserialize<List<VariantInput>>(json, _jsonOptions) ?? new();
                return await NormalizeVariantsAsync(parsedFallback, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (new List<PromptVariant>(), ex.Message);
            }
        }

        if (parameters.TryGetValue("variants", out var variantsObj))
        {
            if (variantsObj is JsonElement el && el.ValueKind == JsonValueKind.Array)
            {
                var parsed = new List<VariantInput>();
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    parsed.Add(VariantInput.FromJsonElement(item));
                }

                return await NormalizeVariantsAsync(parsed, ct);
            }

            if (variantsObj is IEnumerable<object> list)
            {
                try
                {
                    // Best-effort: try to serialize and parse.
                    var serialized = JsonSerializer.Serialize(list, _jsonOptions);
                    var parsed = JsonSerializer.Deserialize<List<VariantInput>>(serialized, _jsonOptions) ?? new();
                    return await NormalizeVariantsAsync(parsed, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return (new List<PromptVariant>(), ex.Message);
                }
            }

            if (variantsObj is string variantsString && !string.IsNullOrWhiteSpace(variantsString))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<VariantInput>>(variantsString, _jsonOptions) ?? new();
                    return await NormalizeVariantsAsync(parsed, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return (new List<PromptVariant>(), ex.Message);
                }
            }
        }

        return (new List<PromptVariant>(), null);
    }

    private async Task<(List<PromptVariant> Variants, string? Error)> NormalizeVariantsAsync(
        List<VariantInput> inputs,
        CancellationToken ct)
    {
        var variants = new List<PromptVariant>();

        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();

            var name = (input.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string systemPrompt;

            if (!string.IsNullOrWhiteSpace(input.SystemPrompt))
            {
                systemPrompt = input.SystemPrompt;
            }
            else if (!string.IsNullOrWhiteSpace(input.Persona))
            {
                var persona = await _personaLoader.LoadPersonaAsync(input.Persona, ct);
                if (persona == null)
                {
                    return (new List<PromptVariant>(), $"Variant '{name}': persona '{input.Persona}' not found");
                }

                systemPrompt = persona.SystemPrompt;
            }
            else
            {
                return (new List<PromptVariant>(), $"Variant '{name}' must specify system_prompt or persona");
            }

            if (!string.IsNullOrWhiteSpace(input.Prepend))
            {
                systemPrompt = input.Prepend + "\n\n" + systemPrompt;
            }

            if (!string.IsNullOrWhiteSpace(input.Append))
            {
                systemPrompt = systemPrompt + "\n\n" + input.Append;
            }

            variants.Add(new PromptVariant(name, systemPrompt));
        }

        return (variants, null);
    }

    private sealed record PromptVariant(string Name, string SystemPrompt);

    private sealed class VariantInput
    {
        public string? Name { get; set; }
        public string? SystemPrompt { get; set; }
        public string? Persona { get; set; }
        public string? Prepend { get; set; }
        public string? Append { get; set; }

        public static VariantInput FromJsonElement(JsonElement obj)
        {
            var input = new VariantInput();

            if (obj.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                input.Name = nameEl.GetString();
            }

            if (TryGetStringProperty(obj, "system_prompt", out var sp) || TryGetStringProperty(obj, "systemPrompt", out sp))
            {
                input.SystemPrompt = sp;
            }

            if (TryGetStringProperty(obj, "persona", out var persona))
            {
                input.Persona = persona;
            }

            if (TryGetStringProperty(obj, "prepend", out var prepend))
            {
                input.Prepend = prepend;
            }

            if (TryGetStringProperty(obj, "append", out var append))
            {
                input.Append = append;
            }

            return input;
        }

        private static bool TryGetStringProperty(JsonElement obj, string propertyName, out string value)
        {
            value = string.Empty;

            if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = el.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
    }

    private sealed record ScoringCriteria(bool MustBeJson, IReadOnlyList<string> ExpectedContains, string? ExpectedRegex);

    private sealed class VariantResult
    {
        public string Name { get; set; } = string.Empty;
        public double AverageScore { get; set; }
        public int SuccessfulTrials { get; set; }
        public List<double> Scores { get; set; } = new();
        public List<string> Samples { get; set; } = new();
        public List<long> DurationsMs { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    private sealed record WinnerSummary(string Name, double AverageScore);

    private sealed class AbTestReport
    {
        public string Task { get; set; } = string.Empty;
        public int Trials { get; set; }
        public ScoringCriteria Criteria { get; set; } = new(false, Array.Empty<string>(), null);
        public List<VariantResult> Results { get; set; } = new();
        public WinnerSummary Winner { get; set; } = new(string.Empty, 0);
    }
}
