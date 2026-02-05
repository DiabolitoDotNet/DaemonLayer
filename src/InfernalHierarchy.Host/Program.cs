using InfernalHierarchy.Agents.Collaboration;
using InfernalHierarchy.Agents.Collaboration.Strategies;
using InfernalHierarchy.Agents.Factory;
using InfernalHierarchy.Agents.Orchestration;
using InfernalHierarchy.Agents.Registry;
using InfernalHierarchy.Agents.Templates;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Core.Eventing;
using InfernalHierarchy.Core.Serialization;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Memory.Embeddings;
using InfernalHierarchy.Memory.Learning;
using InfernalHierarchy.Memory.Maintenance;
using InfernalHierarchy.Memory.Storage;
using InfernalHierarchy.Memory.Vector;
using InfernalHierarchy.Messaging.Bus;
using InfernalHierarchy.Personas.Loading;
using InfernalHierarchy.Telegram.DependencyInjection;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Telegram.Services;
using InfernalHierarchy.Tools.Clients;
using InfernalHierarchy.Tools.Execution;
using InfernalHierarchy.Tools.Learning;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Telemetry;
using InfernalHierarchy.Tools.Tools.Agent;
using InfernalHierarchy.Tools.Tools.Collaboration;
using InfernalHierarchy.Tools.Tools.Experiments;
using InfernalHierarchy.Tools.Tools.Memory;
using InfernalHierarchy.Tools.Tools.Search;
using InfernalHierarchy.Tools.Tools.Telegram;
using InfernalHierarchy.Tools.Tools.Templates;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Configuration.Validation;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using InfernalHierarchy.Tools.Clients.Search;
using Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// HTTP endpoint options
builder.Services.Configure<HttpEndpointOptions>(builder.Configuration.GetSection("Http"));
var httpOptions = builder.Configuration.GetSection("Http").Get<HttpEndpointOptions>() ?? new HttpEndpointOptions();
if (httpOptions.Enabled && !string.IsNullOrWhiteSpace(httpOptions.Urls))
{
    builder.WebHost.UseUrls(httpOptions.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

// Configure Serilog with enrichers
var agentContextEnricher = new AgentContextEnricher();
var messageContextEnricher = new MessageContextEnricher();
var toolContextEnricher = new ToolContextEnricher();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.With(new LoggingEnricher())
    .Enrich.With(agentContextEnricher)
    .Enrich.With(messageContextEnricher)
    .Enrich.With(toolContextEnricher)
    .CreateLogger();

builder.Services.AddSerilog();

// Register enrichers as singletons for DI
builder.Services.AddSingleton(agentContextEnricher);
builder.Services.AddSingleton(messageContextEnricher);
builder.Services.AddSingleton(toolContextEnricher);

// Register configuration sections + validation
builder.Services.AddSingleton<IValidateOptions<OllamaOptions>, OllamaOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<TelegramOptions>, TelegramOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<MemoryOptions>, MemoryOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<HierarchyOptions>, HierarchyOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<SearXNGOptions>, SearXngOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<BraveSearchOptions>, BraveSearchOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<SearXNGOptions>, WebSearchProvidersValidator>();
builder.Services.AddSingleton<IValidateOptions<BraveSearchOptions>, WebSearchProvidersValidator>();

builder.Services.AddOptions<OllamaOptions>()
    .Bind(builder.Configuration.GetSection("Ollama"))
    .ValidateOnStart();
builder.Services.AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection("Telegram"))
    .ValidateOnStart();
builder.Services.AddOptions<MemoryOptions>()
    .Bind(builder.Configuration.GetSection("Memory"))
    .ValidateOnStart();
builder.Services.AddOptions<HierarchyOptions>()
    .Bind(builder.Configuration.GetSection("Hierarchy"))
    .ValidateOnStart();
builder.Services.AddOptions<SearXNGOptions>()
    .Bind(builder.Configuration.GetSection("SearXNG"))
    .ValidateOnStart();
builder.Services.AddOptions<BraveSearchOptions>()
    .Bind(builder.Configuration.GetSection("BraveSearch"))
    .ValidateOnStart();
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("LlmOptions"));
builder.Services.Configure<VectorMemoryOptions>(builder.Configuration.GetSection("VectorMemoryOptions"));
builder.Services.Configure<MemoryPruningOptions>(builder.Configuration.GetSection("MemoryPruningOptions"));
builder.Services.Configure<MemoryLearningOptions>(builder.Configuration.GetSection("MemoryLearningOptions"));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("RagOptions"));
builder.Services.Configure<ReActOptions>(builder.Configuration.GetSection("ReActOptions"));
builder.Services.Configure<OpenTelemetryExportOptions>(builder.Configuration.GetSection("OpenTelemetry:Exporters"));

// Register resource limits
var resourceLimits = new ResourceLimits();
builder.Configuration.GetSection("ResourceLimits").Bind(resourceLimits);
builder.Services.AddSingleton(resourceLimits);
builder.Services.AddSingleton<ResourceLimitService>();

// Security and reliability
builder.Services.AddSingleton<ToolAuthorizationService>();
builder.Services.AddSingleton<TelegramBotClientFactory>();
builder.Services.AddSingleton<ITelegramBotClientFactory>(sp => sp.GetRequiredService<TelegramBotClientFactory>());

// Metrics and observability
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddSingleton<PerformanceMonitor>();
builder.Services.AddSingleton<DistributedTracing>();

// OpenTelemetry configuration
var otelExporterOptions = builder.Configuration.GetSection("OpenTelemetry:Exporters").Get<OpenTelemetryExportOptions>() ?? new OpenTelemetryExportOptions();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "InfernalHierarchy", serviceVersion: "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("InfernalHierarchy")
            .AddHttpClientInstrumentation();

        if (otelExporterOptions.Console.Enabled)
        {
            tracing.AddConsoleExporter();
        }

        if (otelExporterOptions.Otlp.Enabled &&
            Uri.TryCreate(otelExporterOptions.Otlp.Endpoint, UriKind.Absolute, out var endpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
        }
    });

// Resilience policies
builder.Services.AddSingleton<ResiliencePolicies>();
builder.Services.AddSingleton<IResiliencePolicyProvider, ResiliencePolicyProvider>();

// Exception handling
builder.Services.AddSingleton<GlobalExceptionHandler>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<OllamaHealthCheck>("ollama", HealthStatus.Degraded, tags: new[] { "llm", "external" })
    .AddCheck<QdrantHealthCheck>("qdrant", HealthStatus.Degraded, tags: new[] { "vector", "external" })
    .AddCheck<TelegramHealthCheck>("telegram", HealthStatus.Degraded, tags: new[] { "bot", "external" })
    .AddCheck<LiteDbHealthCheck>("litedb", HealthStatus.Unhealthy, tags: new[] { "database", "storage" })
    .AddCheck<AgentHierarchyHealthCheck>("agents", HealthStatus.Degraded, tags: new[] { "agents", "system" });

// Register HttpClientFactory for health checks
builder.Services.AddHttpClient();

// Core services
builder.Services.AddSingleton<IMessageBus, ChannelMessageBus>();
builder.Services.AddSingleton<ISharedMemory, LiteDbSharedMemory>();
builder.Services.AddSingleton<IPersonaLoader, JsonPersonaLoader>();

// Agent system
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddSingleton<IAgentRegistry>(sp => sp.GetRequiredService<AgentRegistry>());
builder.Services.AddSingleton<IAgentFactory, AgentFactory>();

// Tool execution pipeline
builder.Services.AddSingleton<IToolExecutionPipeline, DefaultToolExecutionPipeline>();

// Tools - inject IServiceProvider for command handlers
builder.Services.AddSingleton<IToolRegistry>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ToolRegistry>>();
    var learningService = sp.GetRequiredService<AgentLearningService>();
    var eventSink = sp.GetService<IAgentEventSink>();
    var pipeline = sp.GetRequiredService<IToolExecutionPipeline>();
    return new ToolRegistry(logger, learningService, sp, eventSink, pipeline);
});
builder.Services.AddSingleton<ILlmClient, OllamaClient>();

// Advanced LLM Services
builder.Services.AddSingleton<MultiModelLlmClient>();
builder.Services.AddSingleton<TokenUsageTracker>();
builder.Services.AddSingleton<AgentLearningService>();

// Advanced Memory Services
builder.Services.AddSingleton<OnnxEmbeddingService>();
builder.Services.AddHttpClient<IVectorMemory, VectorMemoryService>();
builder.Services.AddHostedService<VectorMemoryInitializationService>();
builder.Services.AddSingleton<ISkillTreeService, SkillTreeService>();
builder.Services.AddHostedService<MemoryPruningService>();
builder.Services.AddHostedService<MemoryLearningService>();

// Agent Collaboration
builder.Services.AddSingleton<IAgentCollaborationService, AgentCollaborationService>();
builder.Services.AddSingleton<IAggregationStrategy, VotingAggregationStrategy>();
builder.Services.AddSingleton<IAggregationStrategy, WeightedVotingAggregationStrategy>();
builder.Services.AddSingleton<IAggregationStrategy, ConsensusAggregationStrategy>();
builder.Services.AddSingleton<IAggregationStrategy, HighestConfidenceAggregationStrategy>();
builder.Services.AddSingleton<IAggregationStrategy, HierarchicalAggregationStrategy>();

// Agent Templates
builder.Services.AddSingleton<ITemplateService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<TemplateService>>();
    var agentFactory = sp.GetRequiredService<IAgentFactory>();
    var skillTreeService = sp.GetRequiredService<ISkillTreeService>();
    var templatesDirectory = Path.Combine(AppContext.BaseDirectory, "../../../../../../templates");
    return new TemplateService(logger, agentFactory, skillTreeService, templatesDirectory);
});

// Event Sourcing
builder.Services.AddSingleton<EventStore>(sp =>
{
    var configuredPath = builder.Configuration.GetValue<string>("EventStore:Path");
    var storePath = string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(builder.Environment.ContentRootPath, "events")
        : (Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(builder.Environment.ContentRootPath, configuredPath));

    var logger = sp.GetRequiredService<ILogger<EventStore>>();
    return new EventStore(storePath, logger);
});
builder.Services.AddSingleton<IAgentEventSink>(sp => sp.GetRequiredService<EventStore>());

// Register search tools
builder.Services.AddHttpClient<ISearXngClient, SearXngClient>();
builder.Services.AddHttpClient<IBraveSearchClient, BraveSearchClient>();
builder.Services.AddSingleton<SearXNGSearchTool>();
builder.Services.AddSingleton<BraveSearchTool>();
builder.Services.AddSingleton<WebSearchTool>();

// Register unified web search as IWebSearchTool
builder.Services.AddSingleton<IWebSearchTool>(sp => sp.GetRequiredService<WebSearchTool>());
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<WebSearchTool>());

// Other tools
builder.Services.AddSingleton<ITool, CreateSubAgentTool>();
builder.Services.AddSingleton<ITool, MemoryReadTool>();
builder.Services.AddSingleton<ITool, MemoryWriteTool>();
builder.Services.AddSingleton<ITool, RequestCollaborationTool>();
builder.Services.AddSingleton<ITool, TelegramSendTool>();
builder.Services.AddSingleton<ITool, CreateAgentFromTemplateTool>();
builder.Services.AddSingleton<ITool, ListTemplatesTool>();
builder.Services.AddSingleton<ITool, PromptAbTestTool>();

// Register all tools in the registry
builder.Services.AddHostedService<ToolRegistrationService>();

// Configuration management
builder.Services.AddHostedService<ConfigurationReloadService>();
builder.Services.AddHostedService<SecretRotationService>();

// Hosted services
builder.Services.AddInfernalTelegramCommandHandlers();
builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddHostedService<AgentOrchestrator>();

var app = builder.Build();

if (httpOptions.Enabled)
{
    // Health endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var payload = new
            {
                status = report.Status.ToString(),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        status = kvp.Value.Status.ToString(),
                        description = kvp.Value.Description,
                        durationMs = kvp.Value.Duration.TotalMilliseconds,
                        data = kvp.Value.Data
                    })
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonDefaults.WebIndented));
        }
    });

    // Prometheus metrics endpoint
    app.MapGet("/metrics", (MetricsService metricsService) =>
    {
        var metrics = metricsService.GetAllMetrics();
        var body = PrometheusMetricsFormatter.Format(metrics);
        return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8");
    });

    app.MapGet("/", () => Results.Text("InfernalHierarchy is running"));
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
