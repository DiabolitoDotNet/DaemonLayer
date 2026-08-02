using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Security;

internal static class OperationalAuthGuard
{
    internal const string HeaderName = "X-Infernal-Operator-Key";

    public static IResult? ForbidIfUnauthorized(HttpContext ctx, bool localOnly, string configuredApiKey)
    {
        if (localOnly)
        {
            return LocalOnlyGuard.ForbidIfNotLoopback(ctx, localOnly: true);
        }

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var provided) || provided.Count != 1)
        {
            return Results.Unauthorized();
        }

        return string.Equals(provided[0], configuredApiKey, StringComparison.Ordinal)
            ? null
            : Results.Unauthorized();
    }
}