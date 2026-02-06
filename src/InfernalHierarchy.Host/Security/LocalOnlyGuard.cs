using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Security;

internal static class LocalOnlyGuard
{
    public static IResult? ForbidIfNotLoopback(HttpContext ctx, bool localOnly)
    {
        if (!localOnly)
        {
            return null;
        }

        return LoopbackGuard.IsLoopback(ctx.Connection.RemoteIpAddress)
            ? null
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}
