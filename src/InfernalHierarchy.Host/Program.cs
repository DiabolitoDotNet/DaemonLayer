using InfernalHierarchy.Host.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var httpOptions = HostConfigure.ConfigureHttpEndpointOptions(builder);

HostDependencyInjection.AddSerilogLogging(builder);

HostDependencyInjection.AddValidatorsAndPostConfigure(builder);
HostAddOptions.AddValidatedOptions(builder);
HostConfigure.ConfigureBoundOptions(builder);

HostDependencyInjection.AddResourceLimits(builder);
HostDependencyInjection.AddSecurityAndReliability(builder);
HostDependencyInjection.AddObservability(builder);
HostDependencyInjection.AddHealthChecks(builder);

HostDependencyInjection.AddCoreServices(builder);
HostDependencyInjection.AddTools(builder);
HostDependencyInjection.AddConfigurationHostedServices(builder);
HostDependencyInjection.AddHostedServices(builder);

var app = builder.Build();

if (httpOptions.Enabled)
{
    var uiOptions = app.Services.GetRequiredService<IOptions<UiInterfaceOptions>>().Value;
    var wsOptions = app.Services.GetRequiredService<IOptions<WebSocketInterfaceOptions>>().Value;
    var voiceOptions = app.Services.GetRequiredService<IOptions<VoiceInterfaceOptions>>().Value;

    HostUsePipeline.UseRequestTiming(app);
    HostUsePipeline.UseWebSocketsIfEnabled(app, wsOptions);
    HostMapEndpoints.MapAll(app, uiOptions, voiceOptions, wsOptions);
}

try
{
    Log.Information("🔥 InfernalHierarchy starting...");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "💀 Host terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
