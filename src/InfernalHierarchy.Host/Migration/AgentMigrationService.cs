using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Core.Serialization;
using InfernalHierarchy.Host.Personas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Migration;

internal sealed class AgentMigrationService
{
    private readonly IAgentRegistry _registry;
    private readonly IAgentFactory _factory;
    private readonly ISharedMemory _memory;
    private readonly PersonaFileStore _personaStore;
    private readonly ILogger<AgentMigrationService> _logger;
    private readonly string? _signingKey;

    // simple in-memory replay guard (per-process)
    private readonly ConcurrentDictionary<string, DateTimeOffset> _importedBundleIds = new(StringComparer.OrdinalIgnoreCase);

    public AgentMigrationService(
        IAgentRegistry registry,
        IAgentFactory factory,
        ISharedMemory memory,
        PersonaFileStore personaStore,
        IConfiguration config,
        ILogger<AgentMigrationService> logger)
    {
        _registry = registry;
        _factory = factory;
        _memory = memory;
        _personaStore = personaStore;
        _logger = logger;

        _signingKey = config["Migration:SigningKey"];
    }

    public async Task<AgentMigrationBundle?> ExportAsync(
        string agentId,
        int factsLimit,
        int tasksLimit,
        int decisionsLimit,
        CancellationToken ct)
    {
        var agent = _registry.GetAgent(agentId);
        if (agent is null)
        {
            return null;
        }

        factsLimit = Clamp(factsLimit, 0, 2000);
        tasksLimit = Clamp(tasksLimit, 0, 2000);
        decisionsLimit = Clamp(decisionsLimit, 0, 2000);

        var personaName = agent.Persona?.Name ?? agent.Name;
        var rawPersona = await _personaStore.TryLoadRawJsonAsync(personaName, ct);
        if (string.IsNullOrWhiteSpace(rawPersona))
        {
            rawPersona = JsonSerializer.Serialize(agent.Persona, JsonDefaults.WebIndented);
        }

        var facts = Array.Empty<AgentMigrationFact>();
        if (factsLimit > 0)
        {
            var visibleFacts = await _memory.GetVisibleFactsAsync(agent.Id, agent.Rank, ct);
            facts = visibleFacts
                .OrderByDescending(f => f.CreatedAt)
                .Take(factsLimit)
                .Select(f => new AgentMigrationFact(
                    Category: f.Category,
                    Content: f.Content,
                    Source: f.Source,
                    Confidence: f.Confidence,
                    Visibility: f.Visibility.ToString(),
                    MinimumRankToView: f.MinimumRankToView?.ToString(),
                    SharedWithAgents: f.SharedWithAgents?.ToArray() ?? Array.Empty<string>()))
                .ToArray();
        }

        var tasks = Array.Empty<AgentMigrationTask>();
        if (tasksLimit > 0)
        {
            var agentTasks = await _memory.GetTasksByAgentAsync(agent.Id, ct);
            tasks = agentTasks
                .OrderByDescending(t => t.CreatedAt)
                .Take(tasksLimit)
                .Select(t => new AgentMigrationTask(
                    Description: t.Description,
                    Status: t.Status.ToString(),
                    Result: t.Result))
                .ToArray();
        }

        var decisions = Array.Empty<AgentMigrationDecision>();
        if (decisionsLimit > 0)
        {
            var recent = await _memory.GetRecentDecisionsAsync(decisionsLimit, ct);
            decisions = recent
                .Where(d => string.Equals(d.CreatedBy, agent.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.CreatedAt)
                .Take(decisionsLimit)
                .Select(d => new AgentMigrationDecision(
                    Context: d.Context,
                    Action: d.Action,
                    Reasoning: d.Reasoning,
                    Outcome: d.Outcome))
                .ToArray();
        }

        var bundleNoSig = new AgentMigrationBundle(
            FormatVersion: "1",
            BundleId: Guid.NewGuid().ToString("N"),
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Source: new AgentMigrationSource(
                AgentId: agent.Id,
                AgentName: agent.Name,
                ParentAgentId: (agent is InfernalHierarchy.Agents.Base.BaseAgent ba) ? ba.ParentAgentId : null),
            PersonaName: personaName,
            PersonaJson: rawPersona,
            AgentRank: agent.Rank.ToString(),
            Facts: facts,
            Tasks: tasks,
            Decisions: decisions,
            Signature: null);

        var signature = TrySign(bundleNoSig);
        return bundleNoSig with { Signature = signature };
    }

    public async Task<(AgentImportResponse? ok, string? error)> ImportAsync(AgentImportRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BundleJson))
        {
            return (null, "Missing bundleJson");
        }

        AgentMigrationBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<AgentMigrationBundle>(request.BundleJson, JsonDefaults.WebCaseInsensitive);
        }
        catch (Exception ex)
        {
            return (null, $"Invalid bundle JSON: {ex.Message}");
        }

        if (bundle is null)
        {
            return (null, "Invalid bundle JSON");
        }

        if (!string.Equals(bundle.FormatVersion, "1", StringComparison.Ordinal))
        {
            return (null, $"Unsupported bundle formatVersion '{bundle.FormatVersion}'");
        }

        if (string.IsNullOrWhiteSpace(bundle.BundleId))
        {
            return (null, "Missing bundleId");
        }

        if (_importedBundleIds.TryGetValue(bundle.BundleId, out _))
        {
            return (null, "Bundle already imported (replay guard)");
        }

        var sigError = ValidateSignature(bundle);
        if (!string.IsNullOrWhiteSpace(sigError))
        {
            return (null, sigError);
        }

        // Decide persona name
        var personaName = string.IsNullOrWhiteSpace(request.PersonaNameOverride)
            ? bundle.PersonaName
            : request.PersonaNameOverride;

        if (string.IsNullOrWhiteSpace(personaName))
        {
            return (null, "Missing personaName");
        }

        // Overwrite checks
    #pragma warning disable CA1308 // Normalize strings to uppercase. We intentionally use lowercase filenames for cross-platform consistency.
        var personaPath = Path.Combine(_personaStore.SoulsDirectory, $"{personaName.ToLowerInvariant()}.json");
    #pragma warning restore CA1308
        if (File.Exists(personaPath) && !request.OverwritePersona)
        {
            return (null, $"Persona '{personaName}' already exists. Set overwritePersona=true to replace it.");
        }

        var saveResult = await _personaStore.SaveRawJsonAsync(personaName, bundle.PersonaJson, ct);
        if (!saveResult.success)
        {
            return (null, saveResult.error ?? "Failed saving persona");
        }

        var rankText = string.IsNullOrWhiteSpace(request.AgentRankOverride)
            ? bundle.AgentRank
            : request.AgentRankOverride;

        if (!Enum.TryParse<AgentRank>(rankText, ignoreCase: true, out var rank))
        {
            rank = AgentRank.Worker;
        }

        var created = await _factory.CreateAgentAsync(personaName, rank, request.ParentAgentId, ct);

        if (request.StartAgent)
        {
            await created.StartAsync(ct);
        }

        var importedFacts = 0;
        var importedTasks = 0;
        var importedDecisions = 0;

        if (request.ImportFacts && bundle.Facts.Count > 0)
        {
            foreach (var fact in bundle.Facts)
            {
                var entry = new Fact
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = created.Id,
                    Category = fact.Category,
                    Content = fact.Content,
                    Source = $"migrated:{bundle.Source.AgentId}:{fact.Source}",
                    Confidence = fact.Confidence,
                    Visibility = MemoryVisibility.Private,
                    SharedWithAgents = new List<string>(),
                    MinimumRankToView = null
                };

                await _memory.AddFactAsync(entry, ct);
                importedFacts++;
            }
        }

        if (request.ImportTasks && bundle.Tasks.Count > 0)
        {
            foreach (var t in bundle.Tasks)
            {
                var entry = new TaskEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = created.Id,
                    AssignedTo = created.Id,
                    Description = t.Description,
                    Status = ParseTaskStatus(t.Status),
                    Result = t.Result,
                    Visibility = MemoryVisibility.Private
                };

                await _memory.AddTaskAsync(entry, ct);
                importedTasks++;
            }
        }

        if (request.ImportDecisions && bundle.Decisions.Count > 0)
        {
            foreach (var d in bundle.Decisions)
            {
                var entry = new Decision
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = created.Id,
                    Context = d.Context,
                    Action = d.Action,
                    Reasoning = d.Reasoning,
                    Outcome = d.Outcome,
                    Visibility = MemoryVisibility.Private
                };

                await _memory.AddDecisionAsync(entry, ct);
                importedDecisions++;
            }
        }

        _importedBundleIds[bundle.BundleId] = DateTimeOffset.UtcNow;
        TrimReplayGuard();

        _logger.LogInformation("Agent migration imported | BundleId={BundleId} NewAgentId={AgentId} Persona={Persona} Rank={Rank} Facts={Facts} Tasks={Tasks} Decisions={Decisions}",
            bundle.BundleId, created.Id, personaName, rank, importedFacts, importedTasks, importedDecisions);

        return (new AgentImportResponse(
            AgentId: created.Id,
            PersonaName: personaName,
            AgentRank: rank.ToString(),
            ImportedFacts: importedFacts,
            ImportedTasks: importedTasks,
            ImportedDecisions: importedDecisions), null);
    }

    private AgentMigrationSignature? TrySign(AgentMigrationBundle bundleNoSig)
    {
        if (string.IsNullOrWhiteSpace(_signingKey))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bundleNoSig, JsonDefaults.Web));
        var keyBytes = Encoding.UTF8.GetBytes(_signingKey);
        var sig = HMACSHA256.HashData(keyBytes, bytes);
        return new AgentMigrationSignature("HMAC-SHA256", Convert.ToBase64String(sig));
    }

    private string? ValidateSignature(AgentMigrationBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(_signingKey))
        {
            // signing not configured; accept unsigned bundles
            return null;
        }

        if (bundle.Signature is null || string.IsNullOrWhiteSpace(bundle.Signature.Value))
        {
            return "Migration signing is enabled but the bundle is unsigned";
        }

        if (!string.Equals(bundle.Signature.Algorithm, "HMAC-SHA256", StringComparison.OrdinalIgnoreCase))
        {
            return $"Unsupported signature algorithm '{bundle.Signature.Algorithm}'";
        }

        var unsigned = bundle with { Signature = null };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(unsigned, JsonDefaults.Web));
        var keyBytes = Encoding.UTF8.GetBytes(_signingKey);
        var expected = HMACSHA256.HashData(keyBytes, bytes);
        var expectedB64 = Convert.ToBase64String(expected);

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(expectedB64),
                Convert.FromBase64String(bundle.Signature.Value)))
        {
            return "Invalid bundle signature";
        }

        return null;
    }

    private void TrimReplayGuard()
    {
        // Keep this cheap: cap to a few thousand.
        const int max = 2000;
        if (_importedBundleIds.Count <= max)
        {
            return;
        }

        foreach (var key in _importedBundleIds.Keys.Take(_importedBundleIds.Count - max))
        {
            _importedBundleIds.TryRemove(key, out _);
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static InfernalHierarchy.Core.Entities.TaskStatus ParseTaskStatus(string status)
    {
        return Enum.TryParse<InfernalHierarchy.Core.Entities.TaskStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : InfernalHierarchy.Core.Entities.TaskStatus.Pending;
    }
}
