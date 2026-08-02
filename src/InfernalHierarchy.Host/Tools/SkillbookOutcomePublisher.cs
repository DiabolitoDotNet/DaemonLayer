using System.Text.Json;
using LiteDB;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace InfernalHierarchy.Host.Tools;

public sealed class SkillbookOutcomePublisher : ICapabilityOutcomePublisher, IDisposable
{
    private const string CollectionName = "skillbook_entries";

    private readonly SkillbookPublishingOptions _options;
    private readonly LiteDatabase _db;
    private readonly ILogger<SkillbookOutcomePublisher> _logger;

    private ILiteCollection<SkillbookEntryDocument> Entries => _db.GetCollection<SkillbookEntryDocument>(CollectionName);

    public SkillbookOutcomePublisher(
        IOptions<SkillbookPublishingOptions> options,
        IOptions<MemoryOptions> memoryOptions,
        ILogger<SkillbookOutcomePublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var configuredDbPath = string.IsNullOrWhiteSpace(_options.DatabasePath)
            ? memoryOptions.Value.DatabasePath
            : _options.DatabasePath;

        var dbPath = ResolvePath(configuredDbPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _db = new LiteDatabase(dbPath);
        Entries.EnsureIndex(x => x.CapabilityId, unique: true);
    }

    public Task RecordOutcomeAsync(CapabilityOutcome outcome, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(outcome.CapabilityId) || string.IsNullOrWhiteSpace(outcome.CapabilityType))
        {
            return Task.CompletedTask;
        }

        var id = outcome.CapabilityId.Trim().ToLowerInvariant();
        var doc = Entries.FindById(id) ?? new SkillbookEntryDocument
        {
            CapabilityId = id,
            CapabilityType = outcome.CapabilityType,
            Version = "1.0.0",
            SourceTask = outcome.SourceTask,
            RiskLevel = outcome.RiskLevel,
            FirstSeenAtUtc = outcome.OccurredAtUtc,
            LastValidatedAtUtc = outcome.OccurredAtUtc,
            SuccessCount = 0,
            LastPublishedSuccessCount = 0
        };

        doc.CapabilityType = outcome.CapabilityType;
        if (!string.IsNullOrWhiteSpace(outcome.SourceTask))
        {
            doc.SourceTask = outcome.SourceTask;
        }

        if (!string.IsNullOrWhiteSpace(outcome.RiskLevel))
        {
            doc.RiskLevel = outcome.RiskLevel;
        }

        doc.LastValidatedAtUtc = outcome.OccurredAtUtc;

        if (outcome.Kind is CapabilityOutcomeKind.CustomToolCreated
            or CapabilityOutcomeKind.CustomToolExecutionSucceeded
            or CapabilityOutcomeKind.SkillPackGranted)
        {
            doc.SuccessCount++;
        }

        doc.LastOutcomeKind = outcome.Kind.ToString();
        Entries.Upsert(doc);

        TrimIfNeeded();
        TryPublishSkillbookEntry(doc);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void TryPublishSkillbookEntry(SkillbookEntryDocument doc)
    {
        if (doc.SuccessCount < Math.Max(1, _options.PromotionMinSuccessCount))
        {
            return;
        }

        if (doc.LastPublishedSuccessCount >= doc.SuccessCount)
        {
            return;
        }

        var version = IncrementPatch(doc.Version);
        doc.Version = version;
        doc.LastPublishedSuccessCount = doc.SuccessCount;
        doc.LastPublishedAtUtc = DateTime.UtcNow;
        Entries.Upsert(doc);

        var directory = ResolvePath(_options.DirectoryPath);
        Directory.CreateDirectory(directory);

        var payload = new
        {
            id = doc.CapabilityId,
            capability_type = doc.CapabilityType,
            version = doc.Version,
            provenance = new
            {
                source_task = doc.SourceTask,
                risk_level = doc.RiskLevel,
                success_count = doc.SuccessCount,
                last_validated_date = doc.LastValidatedAtUtc.ToString("O")
            },
            audit = new
            {
                first_seen_at_utc = doc.FirstSeenAtUtc.ToString("O"),
                last_published_at_utc = doc.LastPublishedAtUtc?.ToString("O") ?? string.Empty,
                last_outcome_kind = doc.LastOutcomeKind
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonDefaults.WebIndented);
        var filePath = Path.Combine(directory, $"{doc.CapabilityId}.json");
        File.WriteAllText(filePath, json);

        _logger.LogInformation(
            "Published skillbook capability {CapabilityId} v{Version} (success_count={SuccessCount})",
            doc.CapabilityId,
            doc.Version,
            doc.SuccessCount);
    }

    private void TrimIfNeeded()
    {
        var maxEntries = Math.Max(100, _options.MaxEntries);
        var current = Entries.LongCount();
        if (current <= maxEntries)
        {
            return;
        }

        var overflow = (int)(current - maxEntries);
        var toDelete = Entries.Query()
            .OrderBy(x => x.FirstSeenAtUtc)
            .Limit(overflow)
            .ToList();

        foreach (var doc in toDelete)
        {
            Entries.Delete(doc.CapabilityId);
        }
    }

    private static string ResolvePath(string configuredPath)
    {
        var trimmed = configuredPath.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmed));
    }

    private static string IncrementPatch(string version)
    {
        if (!Version.TryParse(version, out var parsed))
        {
            return "1.0.1";
        }

        var patch = Math.Max(0, parsed.Build) + 1;
        return $"{parsed.Major}.{parsed.Minor}.{patch}";
    }

    private sealed class SkillbookEntryDocument
    {
        [BsonId]
        public string CapabilityId { get; set; } = string.Empty;

        public string CapabilityType { get; set; } = string.Empty;

        public string Version { get; set; } = "1.0.0";

        public string SourceTask { get; set; } = string.Empty;

        public string RiskLevel { get; set; } = "Medium";

        public int SuccessCount { get; set; }

        public int LastPublishedSuccessCount { get; set; }

        public DateTime FirstSeenAtUtc { get; set; }

        public DateTime LastValidatedAtUtc { get; set; }

        public DateTime? LastPublishedAtUtc { get; set; }

        public string LastOutcomeKind { get; set; } = string.Empty;
    }
}
