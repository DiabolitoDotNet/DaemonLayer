using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace InfernalHierarchy.Host.Infrastructure;

internal static class HostMapEndpoints
{
    public static void MapAll(WebApplication app, UiInterfaceOptions uiOptions, VoiceInterfaceOptions voiceOptions, WebSocketInterfaceOptions wsOptions)
    {
        if (wsOptions.Enabled)
        {
            WebSocketInterface.Map(app);
        }

        UiInterface.Map(app, uiOptions);
        VoiceApi.Map(app, voiceOptions);

        HealthEndpoints.Map(app);
        MetricsEndpoint.Map(app);

        PerfApi.Map(app, uiOptions);
        AgentsApi.Map(app, uiOptions);

        ToolsApi.Map(app);
        EventsApi.Map(app);

        PersonasApi.Map(app, uiOptions);
        DocsApi.Map(app, uiOptions);

        app.MapGet("/", () => Results.Text("InfernalHierarchy is running"));

        ChatApi.Map(app);
    }
}
