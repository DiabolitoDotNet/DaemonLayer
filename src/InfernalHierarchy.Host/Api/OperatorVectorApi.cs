using InfernalHierarchy.Core.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace InfernalHierarchy.Host.Api;

internal static class OperatorVectorApi
{
    private const string OperatorKeyHeaderName = "X-Infernal-Operator-Key";

    public static void Map(WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;
        if (!options.Enabled)
        {
            return;
        }

        var group = app.MapGroup("/api/ops/vector");

        group.MapPost("/smoke", SmokeAsync)
            .AddEndpointFilter(async (context, next) =>
            {
                var httpContext = context.HttpContext;
                if (!httpContext.Request.Headers.TryGetValue(OperatorKeyHeaderName, out var provided) ||
                    provided.Count != 1)
                {
                    return Results.Unauthorized();
                }

                if (!string.Equals(provided[0], options.ApiKey, StringComparison.Ordinal))
                {
                    return Results.Unauthorized();
                }

                return await next(context);
            });
    }

    private static async Task<IResult> SmokeAsync(
        OperatorVectorSmokeRequest request,
        IVectorMemory vectorMemory,
        IOptions<VectorMemoryOptions> vectorOptions,
        OnnxEmbeddingService embeddingService,
        CancellationToken ct)
    {
        if (!vectorOptions.Value.Enabled)
        {
            return Results.BadRequest(new { error = "VectorMemoryOptions:Enabled is false" });
        }

        var content = string.IsNullOrWhiteSpace(request.Content)
            ? "Vector smoke test fact: the quick brown fox jumps over the lazy dog."
            : request.Content.Trim();

        var query = string.IsNullOrWhiteSpace(request.Query)
            ? content
            : request.Query.Trim();

        var fact = new Fact
        {
            Category = request.Category ?? "ops_smoke",
            Content = content,
            Source = request.Source ?? "operator_api",
            Confidence = request.Confidence ?? 1.0,
            CreatedBy = request.CreatedBy ?? "operator",
            CreatedAt = DateTime.UtcNow,
            Visibility = MemoryVisibility.Public
        };

        await vectorMemory.IndexFactAsync(fact, ct).ConfigureAwait(false);

        var results = await vectorMemory.SearchSimilarVisibleFactsAsync(
            query: query,
            requestingAgentId: "operator",
            requestingAgentRank: AgentRank.Supreme,
            limit: request.Limit ?? 5,
            minScore: request.MinScore ?? 0.70,
            ct: ct).ConfigureAwait(false);

        var probe = await embeddingService.ProbeAsync(ct).ConfigureAwait(false);

        var response = new
        {
            factId = fact.Id,
            vector = new
            {
                enabled = vectorOptions.Value.Enabled,
                qdrantUrl = vectorOptions.Value.QdrantUrl.ToString(),
                collection = vectorOptions.Value.CollectionName,
                dimensions = vectorOptions.Value.VectorDimensions,
            },
            embeddings = new
            {
                probe.Enabled,
                probe.ModelPath,
                probe.TokenizerPath,
                probe.ModelLoaded,
                probe.TokenizerLoaded,
                probe.UsingFallback,
                probe.EmbeddingDimension,
                probe.MaxSequenceLength
            },
            query,
            hits = results.Select(f => new
            {
                f.Id,
                f.Category,
                preview = f.Content.Length > 180 ? f.Content[..180] + "..." : f.Content,
                f.Source,
                f.Confidence,
                f.CreatedAt
            }).ToList()
        };

        return Results.Ok(response);
    }
}

internal sealed record OperatorVectorSmokeRequest(
    string? Content,
    string? Query,
    string? Category,
    string? Source,
    double? Confidence,
    string? CreatedBy,
    int? Limit,
    double? MinScore);
