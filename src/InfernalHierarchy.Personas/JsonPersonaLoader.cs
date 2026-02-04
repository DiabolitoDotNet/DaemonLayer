using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace InfernalHierarchy.Personas;

/// <summary>
/// Loads demon personas from JSON files in the souls/ directory
/// </summary>
public class JsonPersonaLoader : IPersonaLoader
{
    private readonly ILogger<JsonPersonaLoader> _logger;
    private readonly string _soulsDirectory;
    private readonly Dictionary<string, Persona> _cache = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public JsonPersonaLoader(ILogger<JsonPersonaLoader> logger, string? customSoulsDirectory = null)
    {
        _logger = logger;

        if (!string.IsNullOrEmpty(customSoulsDirectory))
        {
            _soulsDirectory = customSoulsDirectory;
        }
        else
        {
            _soulsDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "souls");
        }

        // Normalize path
        _soulsDirectory = Path.GetFullPath(_soulsDirectory);

        if (!Directory.Exists(_soulsDirectory))
        {
            _logger.LogWarning("Souls directory not found at {Path}, creating it", _soulsDirectory);
            Directory.CreateDirectory(_soulsDirectory);
        }

        _logger.LogInformation("📚 PersonaLoader initialized. Souls directory: {Path}", _soulsDirectory);
    }

    public async Task<Persona?> LoadPersonaAsync(string name, CancellationToken ct = default)
    {
        var normalizedName = name.ToLowerInvariant();
        
        // Check cache first
        if (_cache.TryGetValue(normalizedName, out var cached))
        {
            return cached;
        }

        var filePath = Path.Combine(_soulsDirectory, $"{normalizedName}.json");

        if (!File.Exists(filePath))
        {
            _logger.LogError("Persona file not found: {Path}", filePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var persona = JsonSerializer.Deserialize<Persona>(json, _jsonOptions);

            if (persona != null)
            {
                _cache[normalizedName] = persona;
                _logger.LogInformation("😈 Loaded persona: {Name} - {Title}", persona.Name, persona.DemonTitle);
            }

            return persona;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persona from {Path}", filePath);
            return null;
        }
    }

    public async Task<IEnumerable<Persona>> LoadAllPersonasAsync(CancellationToken ct = default)
    {
        var personas = new List<Persona>();

        var files = Directory.GetFiles(_soulsDirectory, "*.json");

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var persona = await LoadPersonaAsync(name, ct);

            if (persona != null)
            {
                personas.Add(persona);
            }
        }

        _logger.LogInformation("Loaded {Count} personas", personas.Count);
        return personas;
    }

    public async Task<bool> ValidatePersonaAsync(string name, CancellationToken ct = default)
    {
        var persona = await LoadPersonaAsync(name, ct);

        if (persona == null) return false;

        // Validation rules
        var isValid = !string.IsNullOrWhiteSpace(persona.Name) &&
                     !string.IsNullOrWhiteSpace(persona.DemonTitle) &&
                     !string.IsNullOrWhiteSpace(persona.SystemPrompt) &&
                     persona.AvailableTools?.Count > 0;

        if (!isValid)
        {
            _logger.LogWarning("Persona {Name} failed validation", name);
        }

        return isValid;
    }
}
