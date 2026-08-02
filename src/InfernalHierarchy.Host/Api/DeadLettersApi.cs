using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class DeadLettersApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;

        app.MapGet("/api/ops/deadletters", async (
            HttpContext ctx,
            IFailedOperationStore store,
            int? limit,
            bool? pendingOnly,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var effectiveLimit = limit is > 0 and <= 1000 ? limit.Value : 100;
            var onlyPending = pendingOnly ?? false;
            var records = await store.GetRecentAsync(effectiveLimit, onlyPending, ct).ConfigureAwait(false);
            var stats = store.GetStats();

            return Results.Ok(new
            {
                stats,
                records
            });
        });

        app.MapPost("/api/ops/deadletters/{id}/replay", async (
            HttpContext ctx,
            string id,
            DeadLetterReplayService replay,
            CancellationToken ct) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var requestedBy = ctx.Connection.RemoteIpAddress?.ToString() ?? "operator";
            var result = await replay.ReplayAsync(id, requestedBy, ct).ConfigureAwait(false);

            if (!result.Available)
            {
                return Results.NotFound(new { id, reason = result.ReasonCode });
            }

            if (!result.Succeeded)
            {
                return Results.BadRequest(new
                {
                    id,
                    reason = result.ReasonCode,
                    error = result.Error
                });
            }

            return Results.Ok(new { id, replayed = true });
        });
    }
}
