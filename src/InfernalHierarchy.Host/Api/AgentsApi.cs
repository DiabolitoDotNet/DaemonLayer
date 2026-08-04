using InfernalHierarchy.Host.Migration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InfernalHierarchy.Host.Api;

internal static class AgentsApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        app.MapGet("/api/agents", (IAgentRegistry registry) =>
        {
            var agents = registry.GetAllAgents()
                .Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    rank = a.Rank.ToString(),
                    status = a.Status.ToString(),
                    parentAgentId = (a is InfernalHierarchy.Agents.Base.BaseAgent ba) ? ba.ParentAgentId : null
                })
                .OrderBy(a => a.rank)
                .ThenBy(a => a.name)
                .ToList();

            return Results.Ok(agents);
        });

        app.MapGet("/api/agents/{agentId}/export", async (
            HttpContext ctx,
            string agentId,
            AgentMigrationService migration,
            int? facts,
            int? tasks,
            int? decisions,
            CancellationToken ct) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var bundle = await migration.ExportAsync(
                agentId,
                factsLimit: facts ?? 200,
                tasksLimit: tasks ?? 200,
                decisionsLimit: decisions ?? 100,
                ct);

            return bundle is null ? Results.NotFound() : Results.Ok(bundle);
        });

        app.MapPost("/api/agents/import", async (
            HttpContext ctx,
            [FromBody] AgentImportRequest req,
            AgentMigrationService migration,
            CancellationToken ct) =>
        {
            var forbid = LocalOnlyGuard.ForbidIfNotLoopback(ctx, uiOptions.LocalOnly);
            if (forbid is not null)
            {
                return forbid;
            }

            var (ok, error) = await migration.ImportAsync(req, ct);
            return ok is not null ? Results.Ok(ok) : Results.BadRequest(new { error });
        });

        app.MapGet("/api/agents/stats", (AgentRegistry registry) => Results.Ok(registry.GetStats()));
    }
}
