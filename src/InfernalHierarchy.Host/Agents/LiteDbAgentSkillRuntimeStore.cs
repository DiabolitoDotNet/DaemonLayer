using LiteDB;
using InfernalHierarchy.Core.Entities;

namespace InfernalHierarchy.Host.Agents;

public sealed class LiteDbAgentSkillRuntimeStore : IAgentSkillRuntimeStore, IDisposable
{
    private const string CollectionName = "agent_skill_runtime_grants";

    private readonly LiteDatabase _db;
    private readonly AgentSkillAssignmentOptions _options;
    private readonly ILogger<LiteDbAgentSkillRuntimeStore> _logger;

    private ILiteCollection<SkillGrantDocument> Grants => _db.GetCollection<SkillGrantDocument>(CollectionName);

    public LiteDbAgentSkillRuntimeStore(
        IOptions<AgentSkillAssignmentOptions> options,
        IOptions<MemoryOptions> memoryOptions,
        ILogger<LiteDbAgentSkillRuntimeStore> logger)
    {
        _options = options.Value;
        _logger = logger;

        var configuredPath = _options.RuntimeGrantDatabasePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = memoryOptions.Value.DatabasePath;
        }

        var absolutePath = ResolveDatabasePath(configuredPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        _db = new LiteDatabase(absolutePath);
        Grants.EnsureIndex(x => x.AgentId);
        Grants.EnsureIndex(x => x.ExpiresAtUtc);

        _logger.LogInformation("Agent skill runtime store initialized at {Path}", absolutePath);
    }

    public void ApplyGrant(string agentId, AgentSkillGrant grant)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("Agent id is required", nameof(agentId));
        }

        ArgumentNullException.ThrowIfNull(grant);

        var now = DateTime.UtcNow;
        var doc = new SkillGrantDocument
        {
            Id = Guid.NewGuid().ToString("n"),
            AgentId = agentId.Trim(),
            SkillPackId = grant.SkillPackId,
            ExpiresAtUtc = grant.ExpiresAtUtc,
            AdditionalTools = grant.AdditionalTools.ToArray(),
            AdditionalSpecializations = grant.AdditionalSpecializations.ToArray(),
            PromptFragments = grant.PromptFragments.ToArray(),
            CreatedAtUtc = now
        };

        Grants.Insert(doc);
        TrimIfNeeded();
        PruneExpired(now);
    }

    public AgentSkillRuntimeOverlay GetOverlay(string agentId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return new AgentSkillRuntimeOverlay();
        }

        var normalizedAgentId = agentId.Trim();
        var active = Grants.Query()
            .Where(g => g.AgentId == normalizedAgentId && g.ExpiresAtUtc > utcNow)
            .ToList();

        if (active.Count == 0)
        {
            return new AgentSkillRuntimeOverlay();
        }

        return new AgentSkillRuntimeOverlay
        {
            ActiveSkillPackIds = active
                .Select(g => g.SkillPackId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AdditionalTools = active
                .SelectMany(g => g.AdditionalTools)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AdditionalSpecializations = active
                .SelectMany(g => g.AdditionalSpecializations)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PromptFragments = active
                .SelectMany(g => g.PromptFragments)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    public int PruneExpired(DateTime utcNow)
    {
        return Grants.DeleteMany(g => g.ExpiresAtUtc <= utcNow);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void TrimIfNeeded()
    {
        var maxEntries = Math.Max(1000, _options.RuntimeGrantMaxEntries);
        var current = Grants.LongCount();
        if (current <= maxEntries)
        {
            return;
        }

        var overflow = (int)(current - maxEntries);
        var toDelete = Grants.Query()
            .OrderBy(x => x.CreatedAtUtc)
            .Limit(overflow)
            .ToList();

        foreach (var doc in toDelete)
        {
            Grants.Delete(doc.Id);
        }
    }

    private static string ResolveDatabasePath(string configuredPath)
    {
        var trimmed = configuredPath.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        var baseDir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, trimmed));
    }

    private sealed class SkillGrantDocument
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("n");

        public string AgentId { get; set; } = string.Empty;

        public string SkillPackId { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public string[] AdditionalTools { get; set; } = Array.Empty<string>();

        public string[] AdditionalSpecializations { get; set; } = Array.Empty<string>();

        public string[] PromptFragments { get; set; } = Array.Empty<string>();

        public DateTime CreatedAtUtc { get; set; }
    }
}
