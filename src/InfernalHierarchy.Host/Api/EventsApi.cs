using InfernalHierarchy.Core.Eventing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Api;

internal static class EventsApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/events", async (EventStore store, int? minutes, CancellationToken ct) =>
        {
            var rangeMinutes = minutes is > 0 and <= 24 * 60 ? minutes.Value : 60;
            var end = DateTime.UtcNow;
            var start = end.AddMinutes(-rangeMinutes);

            var events = await store.GetEventsByTimeRangeAsync(start, end, ct);
            var trimmed = events.TakeLast(500);
            return Results.Ok(trimmed);
        });
    }
}
