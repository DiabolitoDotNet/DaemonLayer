using InfernalHierarchy.Agents.ReAct;
using Microsoft.AspNetCore.Hosting;

namespace InfernalHierarchy.Host.Infrastructure;

internal static class HostConfigure
{
    public static HttpEndpointOptions ConfigureHttpEndpointOptions(WebApplicationBuilder builder)
    {
        builder.Services.Configure<HttpEndpointOptions>(builder.Configuration.GetSection("Http"));
        var httpOptions = builder.Configuration.GetSection("Http").Get<HttpEndpointOptions>() ?? new HttpEndpointOptions();

        if (httpOptions.Enabled && !string.IsNullOrWhiteSpace(httpOptions.Urls))
        {
            builder.WebHost.UseUrls(httpOptions.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return httpOptions;
    }

    public static void ConfigureBoundOptions(WebApplicationBuilder builder)
    {
        ConfigureLlmAndAgentOptions(builder);
        ConfigureMemoryMaintenanceOptions(builder);
        ConfigureObservabilityOptions(builder);
    }

    private static void ConfigureLlmAndAgentOptions(WebApplicationBuilder builder)
    {
        builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("LlmOptions"));
        builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("RagOptions"));
        builder.Services.Configure<ReActOptions>(builder.Configuration.GetSection("ReActOptions"));
    }

    private static void ConfigureMemoryMaintenanceOptions(WebApplicationBuilder builder)
    {
        builder.Services.Configure<MemoryPruningOptions>(builder.Configuration.GetSection("MemoryPruningOptions"));
        builder.Services.Configure<MemoryLearningOptions>(builder.Configuration.GetSection("MemoryLearningOptions"));
    }

    private static void ConfigureObservabilityOptions(WebApplicationBuilder builder)
    {
        builder.Services.Configure<OpenTelemetryExportOptions>(builder.Configuration.GetSection("OpenTelemetry:Exporters"));
        builder.Services.Configure<TraceCaptureOptions>(builder.Configuration.GetSection("Perf:TraceCapture"));
        builder.Services.Configure<PerfRequestProfilingOptions>(builder.Configuration.GetSection("Perf:RequestProfiling"));
    }
}
