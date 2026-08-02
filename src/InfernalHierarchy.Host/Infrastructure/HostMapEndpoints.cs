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
        MetricsEndpoint.Map(app, uiOptions);

        PerfApi.Map(app, uiOptions);
        TimelineApi.Map(app, uiOptions);
        AgentsApi.Map(app, uiOptions);

        ToolsApi.Map(app, uiOptions);
        EventsApi.Map(app, uiOptions);
        DeadLettersApi.Map(app, uiOptions);

        PersonasApi.Map(app, uiOptions);
        DocsApi.Map(app, uiOptions);
        PlaygroundApi.Map(app, uiOptions);

        OperatorVectorApi.Map(app);

        app.MapGet("/", () => Results.Text("InfernalHierarchy is running"));

        ChatApi.Map(app, uiOptions);
    }
}
