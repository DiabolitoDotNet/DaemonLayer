using System.Text.Json;
using System.Text.RegularExpressions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Personas;

internal sealed class PersonaFileStore
{
    private static readonly Regex SafePersonaFileName = new("^[a-zA-Z0-9_-]{1,50}$", RegexOptions.Compiled);

    private readonly ILogger<PersonaFileStore> _logger;
    private readonly string _soulsDirectory;

    public PersonaFileStore(ILogger<PersonaFileStore> logger, IConfiguration config)
    {
        _logger = logger;

        var customDir = config["Personas:SoulsDirectory"];
        if (!string.IsNullOrWhiteSpace(customDir))
        {
            _soulsDirectory = Path.GetFullPath(customDir);
        }
        else
        {
            _soulsDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "souls"));
        }

        if (!Directory.Exists(_soulsDirectory))
        {
            _logger.LogWarning("Souls directory not found at {Path}, creating it", _soulsDirectory);
            Directory.CreateDirectory(_soulsDirectory);
        }

        _logger.LogInformation("PersonaFileStore initialized. Souls directory: {Path}", _soulsDirectory);
    }

    public string SoulsDirectory => _soulsDirectory;

    public IEnumerable<PersonaFileSummary> List()
    {
        if (!Directory.Exists(_soulsDirectory))
        {
            return Array.Empty<PersonaFileSummary>();
        }

        return Directory
            .GetFiles(_soulsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new PersonaFileSummary(
                    name: Path.GetFileNameWithoutExtension(path),
                    path: path,
                    lastWriteTimeUtc: info.LastWriteTimeUtc,
                    lengthBytes: info.Length);
            })
            .OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string?> TryLoadRawJsonAsync(string name, CancellationToken ct)
    {
        if (!TryNormalizeName(name, out var normalized))
        {
            return null;
        }

        var path = GetPersonaPath(normalized);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task<Persona?> TryLoadPersonaAsync(string name, CancellationToken ct)
    {
        var raw = await TryLoadRawJsonAsync(name, ct);
        if (raw is null)
        {
            return null;
        }

        try
        {
            raw = InputValidator.SanitizeJson(raw);
            return JsonSerializer.Deserialize<Persona>(raw, JsonDefaults.WebCaseInsensitive);
        }
        catch
        {
            return null;
        }
    }

    public async Task<PersonaWriteResult> SaveRawJsonAsync(string routeName, string rawJson, CancellationToken ct)
    {
        var validation = ValidateRawJson(routeName, rawJson);
        if (!validation.success)
        {
            return PersonaWriteResult.Invalid(validation.error ?? "Persona failed validation", validation.issues);
        }

        var path = GetPersonaPath(validation.normalizedName!);

        Directory.CreateDirectory(_soulsDirectory);
        await File.WriteAllTextAsync(path, validation.formattedJson!, ct);

        _logger.LogInformation("Persona saved: {Name} → {Path}", validation.normalizedName, path);

        return PersonaWriteResult.Ok(path);
    }

    public static PersonaValidationResult ValidateRawJson(string routeName, string rawJson)
    {
        if (!TryNormalizeName(routeName, out var normalized))
        {
            return PersonaValidationResult.Invalid("Invalid persona name. Use letters/numbers/_/-, max 50 chars.");
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return PersonaValidationResult.Invalid("Missing JSON body");
        }

        if (rawJson.Length > 250_000)
        {
            return PersonaValidationResult.Invalid("JSON too large (max 250000 chars)");
        }

        rawJson = InputValidator.SanitizeJson(rawJson);

        Persona? persona;
        try
        {
            persona = JsonSerializer.Deserialize<Persona>(rawJson, JsonDefaults.WebCaseInsensitive);
        }
        catch (Exception ex)
        {
            return PersonaValidationResult.Invalid($"Invalid JSON: {ex.Message}");
        }

        if (persona is null)
        {
            return PersonaValidationResult.Invalid("Invalid JSON: could not deserialize persona");
        }

        persona.Name = normalized;

        var issues = Validate(persona);
        if (issues.Count > 0)
        {
            return PersonaValidationResult.Invalid("Persona failed validation", issues);
        }

        var formatted = JsonSerializer.Serialize(persona, JsonDefaults.WebIndented);
        return PersonaValidationResult.Ok(normalized, formatted);
    }

    private string GetPersonaPath(string normalizedName)
    {
#pragma warning disable CA1308 // Normalize strings to uppercase. We intentionally use lowercase filenames for cross-platform consistency.
        var fileName = normalizedName.ToLowerInvariant();
#pragma warning restore CA1308
        return Path.Combine(_soulsDirectory, $"{fileName}.json");
    }

    private static bool TryNormalizeName(string name, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (!SafePersonaFileName.IsMatch(trimmed))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    private static List<string> Validate(Persona persona)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(persona.Name)) issues.Add("name is required");
        if (string.IsNullOrWhiteSpace(persona.DemonTitle)) issues.Add("demonTitle is required");
        if (string.IsNullOrWhiteSpace(persona.SystemPrompt)) issues.Add("systemPrompt is required");
        if (persona.AvailableTools is null || persona.AvailableTools.Count == 0) issues.Add("availableTools must contain at least 1 tool");

        return issues;
    }
}

internal sealed record PersonaFileSummary(string name, string path, DateTime lastWriteTimeUtc, long lengthBytes);

internal sealed record PersonaWriteResult(bool success, string? path, string? error, IReadOnlyList<string> issues)
{
    public static PersonaWriteResult Ok(string path) => new(true, path, null, Array.Empty<string>());

    public static PersonaWriteResult Invalid(string error, IReadOnlyList<string>? issues = null)
        => new(false, null, error, issues ?? Array.Empty<string>());
}

internal sealed record PersonaValidationResult(bool success, string? normalizedName, string? formattedJson, string? error, IReadOnlyList<string> issues)
{
    public static PersonaValidationResult Ok(string normalizedName, string formattedJson)
        => new(true, normalizedName, formattedJson, null, Array.Empty<string>());

    public static PersonaValidationResult Invalid(string error, IReadOnlyList<string>? issues = null)
        => new(false, null, null, error, issues ?? Array.Empty<string>());
}
