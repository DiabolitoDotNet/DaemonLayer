using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Embeddings;
using InfernalHierarchy.Memory.Storage;
using InfernalHierarchy.Memory.Vector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public class VectorMemoryServiceLiveQdrantTests
{
    [Fact]
    public async Task LiveQdrant_VectorIndexAndSearch_ShouldRoundTripFact()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("INFERNAL_LIVE_QDRANT"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var qdrantUrl = Environment.GetEnvironmentVariable("INFERNAL_QDRANT_URL")?.Trim();
        if (string.IsNullOrWhiteSpace(qdrantUrl))
        {
            qdrantUrl = "http://localhost:6333";
        }

        var collectionName = $"infernal_live_test_{Guid.NewGuid():N}";
        var dbPath = Path.Combine(Path.GetTempPath(), $"infernal-live-{Guid.NewGuid():N}.db");

        try
        {
            using var httpClient = new HttpClient();

            var vectorOptions = new VectorMemoryOptions
            {
                Enabled = true,
                QdrantUrl = new Uri(qdrantUrl),
                CollectionName = collectionName,
                VectorDimensions = 384,
            };

            var memoryOptions = Options.Create(new MemoryOptions { DatabasePath = dbPath });
            using var sharedMemory = new LiteDbSharedMemory(memoryOptions, NullLogger<LiteDbSharedMemory>.Instance);

            using var embeddingService = new OnnxEmbeddingService(
                Options.Create(new OnnxEmbeddingOptions
                {
                    Enabled = true,
                    ModelPath = Environment.GetEnvironmentVariable("INFERNAL_ONNX_MODEL_PATH")?.Trim()
                        ?? "./models/sentence-transformers/model.onnx",
                    TokenizerPath = Environment.GetEnvironmentVariable("INFERNAL_ONNX_TOKENIZER_PATH")?.Trim()
                        ?? "./models/sentence-transformers/tokenizer.json",
                    EmbeddingDimension = 384,
                    MaxSequenceLength = 128,
                }),
                NullLogger<OnnxEmbeddingService>.Instance);

            var sut = new VectorMemoryService(
                httpClient,
                Options.Create(vectorOptions),
                sharedMemory,
                embeddingService,
                NullLogger<VectorMemoryService>.Instance);

            await sut.InitializeCollectionAsync();

            var probe = await embeddingService.ProbeAsync();
            var hasAssets = File.Exists(probe.ModelPath) && File.Exists(probe.TokenizerPath);
            if (hasAssets)
            {
                probe.UsingFallback.Should().BeFalse("model/tokenizer assets exist and should load for the live test");
            }

            var text = "The InfernalHierarchy vector memory roundtrip test.";

            var fact = new Fact
            {
                Category = "live_test",
                Content = text,
                Source = "VectorMemoryServiceLiveQdrantTests",
                Confidence = 0.95,
                CreatedBy = "lucifer",
                CreatedAt = DateTime.UtcNow,
                Visibility = MemoryVisibility.Public
            };

            await sut.IndexFactAsync(fact);

            // Query with the same text: works for both ONNX embeddings and fallback embeddings.
            var results = await sut.SearchSimilarVisibleFactsAsync(
                query: text,
                requestingAgentId: "lucifer",
                requestingAgentRank: AgentRank.Supreme,
                limit: 5,
                minScore: 0.70);

            results.Should().NotBeEmpty();
            results.Select(r => r.Id).Should().Contain(fact.Id);
        }
        finally
        {
            try
            {
                var uri = new Uri(new Uri(qdrantUrl.TrimEnd('/')), $"/collections/{collectionName}");
                using var cleanupClient = new HttpClient { BaseAddress = new Uri(qdrantUrl) };
                await cleanupClient.DeleteAsync(uri);
            }
            catch
            {
                // best-effort cleanup
            }

            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
