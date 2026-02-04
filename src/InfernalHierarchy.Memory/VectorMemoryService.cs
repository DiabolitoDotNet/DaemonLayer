using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace InfernalHierarchy.Memory;

/// <summary>
/// Vector-based semantic memory using Qdrant for similarity search
/// </summary>
public sealed class VectorMemoryService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VectorMemoryService> _logger;
    private readonly VectorMemoryOptions _options;
    private readonly ISharedMemory _sharedMemory;
    private readonly OnnxEmbeddingService _embeddingService;

    public VectorMemoryService(
        HttpClient httpClient,
        IOptions<VectorMemoryOptions> options,
        ISharedMemory sharedMemory,
        OnnxEmbeddingService embeddingService,
        ILogger<VectorMemoryService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _sharedMemory = sharedMemory;
        _embeddingService = embeddingService;

        _httpClient.BaseAddress = _options.QdrantUrl;
    }

    /// <summary>
    /// Initialize Qdrant collection for vector storage
    /// </summary>
    public async Task InitializeCollectionAsync(CancellationToken ct = default)
    {
        try
        {
            var collectionName = _options.CollectionName;

            // Check if collection exists
            var response = await _httpClient.GetAsync($"/collections/{collectionName}", ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("📦 Qdrant collection '{Collection}' already exists", collectionName);
                return;
            }

            // Create collection
            var createRequest = new
            {
                vectors = new
                {
                    size = _options.VectorDimensions,
                    distance = "Cosine"
                }
            };

            var createResponse = await _httpClient.PutAsJsonAsync($"/collections/{collectionName}", createRequest, ct);
            createResponse.EnsureSuccessStatusCode();

            _logger.LogInformation("✅ Created Qdrant collection '{Collection}' with {Dimensions}D vectors",
                collectionName, _options.VectorDimensions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Qdrant collection");
            throw;
        }
    }

    /// <summary>
    /// Store fact with vector embedding
    /// </summary>
    public async Task StoreFactWithVectorAsync(Fact fact, float[] embedding, CancellationToken ct = default)
    {
        try
        {
            // Store fact in LiteDB
            await _sharedMemory.AddFactAsync(fact, ct);

            // Store vector in Qdrant
            var point = new
            {
                id = fact.Id,
                vector = embedding,
                payload = new
                {
                    fact.Category,
                    fact.Content,
                    fact.Source,
                    fact.Confidence,
                    fact.CreatedBy,
                    fact.CreatedAt
                }
            };

            var upsertRequest = new
            {
                points = new[] { point }
            };

            var response = await _httpClient.PutAsJsonAsync(
                $"/collections/{_options.CollectionName}/points",
                upsertRequest,
                ct);

            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Stored fact {FactId} with vector embedding", fact.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store fact with vector");
            throw;
        }
    }

    /// <summary>
    /// Search for similar facts using vector similarity
    /// </summary>
    public async Task<IEnumerable<Fact>> SearchSimilarAsync(
        float[] queryEmbedding,
        int limit = 10,
        double minScore = 0.7,
        CancellationToken ct = default)
    {
        try
        {
            var searchRequest = new
            {
                vector = queryEmbedding,
                limit,
                score_threshold = minScore,
                with_payload = true
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/collections/{_options.CollectionName}/points/search",
                searchRequest,
                ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(ct);
            if (result?.Result == null)
            {
                return Enumerable.Empty<Fact>();
            }

            // Retrieve full facts from LiteDB
            var factIds = result.Result.Select(r => r.Id).ToList();
            var facts = new List<Fact>();

            foreach (var id in factIds)
            {
                var fact = await _sharedMemory.GetFactAsync(id, ct);
                if (fact != null)
                {
                    facts.Add(fact);
                }
            }

            _logger.LogDebug("Found {Count} similar facts", facts.Count);
            return facts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search similar facts");
            return Enumerable.Empty<Fact>();
        }
    }

    /// <summary>
    /// Generate embeddings using ONNX sentence-transformers model
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        return await _embeddingService.GenerateEmbeddingAsync(text, ct);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _embeddingService?.Dispose();
    }

    private class QdrantSearchResponse
    {
        public List<QdrantSearchResult>? Result { get; set; }
    }

    private class QdrantSearchResult
    {
        public string Id { get; set; } = string.Empty;
        public double Score { get; set; }
        public JsonElement? Payload { get; set; }
    }
}

public class VectorMemoryOptions
{
    public Uri QdrantUrl { get; set; } = new Uri("http://localhost:6333");
    public string CollectionName { get; set; } = "infernal_facts";
    public int VectorDimensions { get; set; } = 384; // Default for sentence-transformers/all-MiniLM-L6-v2
    public bool Enabled { get; set; }
}
