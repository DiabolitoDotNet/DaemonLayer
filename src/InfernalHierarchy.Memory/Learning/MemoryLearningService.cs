using Microsoft.Extensions.Hosting;

namespace InfernalHierarchy.Memory.Learning;

/// <summary>
/// Background service that improves memory quality over time by:
/// - compressing overly-long facts
/// - clustering public facts and producing summary facts
///
/// Disabled by default.
/// </summary>
public sealed class MemoryLearningService : BackgroundService
{
    private readonly ISharedMemory _sharedMemory;
    private readonly IVectorMemory _vectorMemory;
    private readonly ILlmClient _llmClient;
    private readonly OnnxEmbeddingService _embeddingService;
    private readonly ILogger<MemoryLearningService> _logger;
    private readonly MemoryLearningOptions _options;

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0;
        double magA = 0;
        double magB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom < 1e-12 ? 0 : dot / denom;
    }

    public MemoryLearningService(
        ISharedMemory sharedMemory,
        IVectorMemory vectorMemory,
        ILlmClient llmClient,
        OnnxEmbeddingService embeddingService,
        IOptions<MemoryLearningOptions> options,
        ILogger<MemoryLearningService> logger)
    {
        _sharedMemory = sharedMemory;
        _vectorMemory = vectorMemory;
        _llmClient = llmClient;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("🧠 Memory learning is disabled");
            return;
        }

        _logger.LogInformation("🧠 Memory learning service started - running every {Minutes}m", _options.IntervalMinutes);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during memory learning");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        _logger.LogInformation("🧠 Starting memory learning pass...");

        if (_options.EnableCompression)
        {
            await CompressLongFactsAsync(ct).ConfigureAwait(false);
        }

        if (_options.EnableClustering)
        {
            await ClusterAndSummarizePublicFactsAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation("✅ Memory learning pass complete");
    }

    private async Task CompressLongFactsAsync(CancellationToken ct)
    {
        var allFacts = (await _sharedMemory.SearchFactsAsync("", ct).ConfigureAwait(false)).ToList();

        var candidates = allFacts
            .Where(f => !string.IsNullOrWhiteSpace(f.Content))
            .Where(f => f.Content.Length > _options.CompressIfLongerThanChars)
            .OrderByDescending(f => f.CreatedAt)
            .Take(_options.MaxFactsPerRun)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        _logger.LogInformation("🗜️ Compressing {Count} long facts", candidates.Count);

        foreach (var fact in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var prompt = $"""
You are compressing a memory fact.

Constraints:
- Do NOT invent new information.
- Preserve key entities, numbers, and unique identifiers.
- Output plain text only (no JSON).
- Max length: {_options.CompressToMaxChars} characters.

Fact:
{fact.Content}
""";

            string compressed;
            try
            {
                compressed = await _llmClient.GetSimpleCompletionAsync(prompt, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Compression failed for fact {FactId}", fact.Id);
                continue;
            }

            compressed = (compressed ?? string.Empty).Trim();
            if (compressed.Length == 0)
            {
                continue;
            }

            if (compressed.Length > _options.CompressToMaxChars)
            {
                compressed = compressed[.._options.CompressToMaxChars];
            }

            if (compressed == fact.Content)
            {
                continue;
            }

            var updated = fact;
            updated.Content = compressed;
            updated.LastModifiedBy = "memory_learning";

            await _sharedMemory.UpdateFactAsync(updated, "Automatic compression", ct).ConfigureAwait(false);
        }
    }

    private async Task ClusterAndSummarizePublicFactsAsync(CancellationToken ct)
    {
        var allFacts = (await _sharedMemory.SearchFactsAsync("", ct).ConfigureAwait(false)).ToList();

        var candidates = allFacts
            .Where(f => f.Visibility == MemoryVisibility.Public)
            .Where(f => !string.IsNullOrWhiteSpace(f.Content))
            .Where(f => !string.Equals(f.Category, _options.SummaryCategory, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.CreatedAt)
            .Take(_options.MaxFactsPerRun)
            .ToList();

        if (candidates.Count < _options.MinClusterSize)
        {
            return;
        }

        var embeddings = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var fact in candidates)
        {
            ct.ThrowIfCancellationRequested();
            embeddings[fact.Id] = await _embeddingService.GenerateEmbeddingAsync(fact.Content, ct).ConfigureAwait(false);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seed in candidates)
        {
            if (visited.Contains(seed.Id))
            {
                continue;
            }

            var cluster = new List<Fact> { seed };
            visited.Add(seed.Id);

            foreach (var other in candidates)
            {
                if (visited.Contains(other.Id))
                {
                    continue;
                }

                var sim = CosineSimilarity(embeddings[seed.Id], embeddings[other.Id]);
                if (sim >= _options.ClusterSimilarityThreshold)
                {
                    cluster.Add(other);
                    visited.Add(other.Id);
                }
            }

            if (cluster.Count < _options.MinClusterSize)
            {
                continue;
            }

            var summary = await SummarizeClusterAsync(cluster, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            if (summary.Length > _options.SummaryMaxChars)
            {
                summary = summary[.._options.SummaryMaxChars];
            }

            var avgConfidence = cluster.Average(f => f.Confidence);
            var clusterIds = string.Join(",", cluster.Select(f => f.Id));

            var summaryFact = new Fact
            {
                CreatedBy = "memory_learning",
                Category = _options.SummaryCategory,
                Content = summary.Trim(),
                Source = $"memory_learning:cluster({clusterIds})",
                Confidence = Math.Clamp(avgConfidence, 0.0, 1.0),
                Visibility = MemoryVisibility.Public
            };

            await _vectorMemory.IndexFactAsync(summaryFact, ct).ConfigureAwait(false);

            _logger.LogInformation("🧠 Created cluster summary fact {SummaryId} for {Count} facts", summaryFact.Id, cluster.Count);
        }
    }

    private async Task<string> SummarizeClusterAsync(List<Fact> cluster, CancellationToken ct)
    {
        var factsText = string.Join("\n\n", cluster.Select(f => $"[{f.Category}] {f.Content}"));

        var prompt = $"""
You are consolidating multiple PUBLIC memory facts into a short, reusable summary.

Constraints:
- Do NOT invent new information.
- Merge duplicates and remove noise.
- Prefer concrete details (names, numbers, identifiers) when present.
- Output plain text only (no JSON).
- Max length: {_options.SummaryMaxChars} characters.

Facts:
{factsText}
""";

        try
        {
            return await _llmClient.GetSimpleCompletionAsync(prompt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cluster summarization failed");
            return string.Empty;
        }
    }
}
