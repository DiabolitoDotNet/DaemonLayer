using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Ui;

internal static class UiInterface
{
    public static void Map(WebApplication app, UiInterfaceOptions uiOptions)
    {
        if (!uiOptions.Enabled)
        {
            return;
        }

        static bool IsAllowed(HttpContext ctx, UiInterfaceOptions options)
            => !options.LocalOnly || LoopbackGuard.IsLoopback(ctx.Connection.RemoteIpAddress);

        static IResult Forbid() => Results.StatusCode(StatusCodes.Status403Forbidden);

        app.MapGet("/ui", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.IndexHtml, "text/html; charset=utf-8");
        });

        app.MapGet("/ui/perf", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.IndexHtml, "text/html; charset=utf-8");
        });

        app.MapGet("/ui/ops", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.IndexHtml, "text/html; charset=utf-8");
        });

        app.MapGet("/ui/personas", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.IndexHtml, "text/html; charset=utf-8");
        });

        app.MapGet("/ui/timeline", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.IndexHtml, "text/html; charset=utf-8");
        });

        app.MapGet("/ui/playground", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.IndexHtml, "text/html; charset=utf-8");
        });

        app.MapGet("/ui/docs", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.IndexHtml, "text/html; charset=utf-8");
        });

        app.MapGet("/ui/migrate", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.IndexHtml, "text/html; charset=utf-8");
        });

        app.MapGet("/ui/app.js", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.AppJs, "application/javascript; charset=utf-8");
        });

        app.MapGet("/ui/styles.css", (HttpContext ctx) =>
        {
            if (!IsAllowed(ctx, uiOptions))
            {
                return Forbid();
            }

            return Results.Text(DashboardAssets.StylesCss, "text/css; charset=utf-8");
        });
    }
}
