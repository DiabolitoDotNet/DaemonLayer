using InfernalHierarchy.Host.Personas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InfernalHierarchy.Host.Api;

internal static class PersonasApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        app.MapGet("/api/personas", (HttpContext ctx, PersonaFileStore store) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var list = store.List().Select(p => new
            {
                name = p.name,
                lastWriteTimeUtc = p.lastWriteTimeUtc,
                lengthBytes = p.lengthBytes
            });

            return Results.Ok(list);
        });

        app.MapGet("/api/personas/{name}", async (HttpContext ctx, string name, PersonaFileStore store, CancellationToken ct) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var raw = await store.TryLoadRawJsonAsync(name, ct);
            if (raw is null)
            {
                return Results.NotFound(new { error = $"Persona '{name}' not found" });
            }

            var parsed = await store.TryLoadPersonaAsync(name, ct);
            return Results.Ok(new
            {
                name,
                json = raw,
                persona = parsed,
                valid = parsed is not null
            });
        });

        app.MapPut("/api/personas/{name}", async (HttpContext ctx, string name, [FromBody] PersonaRawUpdateRequest req, PersonaFileStore store, CancellationToken ct) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var result = await store.SaveRawJsonAsync(name, req.Json, ct);
            if (!result.success)
            {
                return Results.BadRequest(new { error = result.error, issues = result.issues });
            }

            return Results.Ok(new { ok = true, path = result.path });
        });

        app.MapPost("/api/personas/{name}/validate", (HttpContext ctx, string name, [FromBody] PersonaRawUpdateRequest req, PersonaFileStore store) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var validation = PersonaFileStore.ValidateRawJson(name, req.Json);
            if (!validation.success)
            {
                return Results.BadRequest(new { error = validation.error, issues = validation.issues });
            }

            return Results.Ok(new { ok = true, normalizedName = validation.normalizedName });
        });
    }
}
