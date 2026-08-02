using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class ToolsApi
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        var operatorOptions = app.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;

        app.MapGet("/api/tools", (HttpContext ctx, IToolRegistry tools) =>
        {
            var forbid = OperationalAuthGuard.ForbidIfUnauthorized(ctx, uiOptions.LocalOnly, operatorOptions.ApiKey);
            if (forbid is not null)
            {
                return forbid;
            }

            var all = tools.GetAllTools()
                .Select(t => new { name = t.Name, description = t.Description })
                .OrderBy(t => t.name)
                .ToList();

            return Results.Ok(all);
        });
    }
}
