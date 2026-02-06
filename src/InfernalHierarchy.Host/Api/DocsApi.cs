using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Docs;
using InfernalHierarchy.Host.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class DocsApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        app.MapGet("/api/docs/markdown", async (HttpContext ctx, DocumentationGenerator docs, CancellationToken ct) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var md = await docs.GenerateMarkdownAsync(ct);
            return Results.Text(md, "text/markdown; charset=utf-8");
        });

        app.MapGet("/api/docs/json", async (HttpContext ctx, DocumentationGenerator docs, CancellationToken ct) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var json = await docs.GenerateJsonAsync(ct);
            return Results.Text(json, "application/json; charset=utf-8");
        });
    }
}
