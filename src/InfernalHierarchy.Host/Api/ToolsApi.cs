using InfernalHierarchy.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class ToolsApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/tools", (IToolRegistry tools) =>
        {
            var all = tools.GetAllTools()
                .Select(t => new { name = t.Name, description = t.Description })
                .OrderBy(t => t.name)
                .ToList();

            return Results.Ok(all);
        });
    }
}
