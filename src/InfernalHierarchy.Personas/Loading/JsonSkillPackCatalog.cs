using System.Text.Json;

namespace InfernalHierarchy.Personas.Loading;

/// <summary>
/// Loads skill packs from JSON files in the skills/ directory.
/// </summary>
public sealed class JsonSkillPackCatalog : ISkillPackCatalog
{
    private readonly ILogger<JsonSkillPackCatalog> _logger;
    private readonly string _skillsDirectory;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record CacheEntry(SkillPack SkillPack, DateTime LastWriteTimeUtc);

    public JsonSkillPackCatalog(ILogger<JsonSkillPackCatalog> logger, string? customSkillsDirectory = null)
    {
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(customSkillsDirectory))
        {
            _skillsDirectory = Path.GetFullPath(customSkillsDirectory);
        }
        else
        {
            _skillsDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "skills"));
        }

        if (!Directory.Exists(_skillsDirectory))
        {
            _logger.LogWarning("Skills directory not found at {Path}, creating it", _skillsDirectory);
            Directory.CreateDirectory(_skillsDirectory);
        }

        _logger.LogInformation("SkillPackCatalog initialized. Skills directory: {Path}", _skillsDirectory);
    }

    public async Task<SkillPack?> GetByIdAsync(string skillPackId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(skillPackId))
        {
            return null;
        }

        var normalizedId = skillPackId.Trim();
        var filePath = Path.Combine(_skillsDirectory, $"{normalizedId}.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(filePath);
        if (_cache.TryGetValue(normalizedId, out var cached) && cached.LastWriteTimeUtc == lastWriteUtc)
        {
            return cached.SkillPack;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var pack = JsonSerializer.Deserialize<SkillPack>(json, JsonOptions);
            if (pack == null || string.IsNullOrWhiteSpace(pack.Id))
            {
                _logger.LogWarning("Skill pack in {Path} is invalid", filePath);
                return null;
            }

            _cache[normalizedId] = new CacheEntry(pack, lastWriteUtc);
            return pack;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load skill pack {SkillPackId} from {Path}", normalizedId, filePath);
            return null;
        }
    }

    public async Task<IReadOnlyList<SkillPack>> GetAllAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_skillsDirectory, "*.json", SearchOption.TopDirectoryOnly);
        var packs = new List<SkillPack>();

        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var pack = await GetByIdAsync(id, ct);
            if (pack != null)
            {
                packs.Add(pack);
            }
        }

        return packs
            .Where(p => p.Enabled)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
