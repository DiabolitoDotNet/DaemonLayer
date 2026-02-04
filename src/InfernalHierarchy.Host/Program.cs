using InfernalHierarchy.Agents;
using InfernalHierarchy.Core;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Memory;
using InfernalHierarchy.Messaging;
using InfernalHierarchy.Personas;
using InfernalHierarchy.Telegram;
using InfernalHierarchy.Tools;
using InfernalHierarchy.Host;
using Serilog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

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

// Register configuration sections
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<MemoryOptions>(builder.Configuration.GetSection("Memory"));
builder.Services.Configure<HierarchyOptions>(builder.Configuration.GetSection("Hierarchy"));
builder.Services.Configure<SearXNGOptions>(builder.Configuration.GetSection("SearXNG"));
builder.Services.Configure<BraveSearchOptions>(builder.Configuration.GetSection("BraveSearch"));
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("LlmOptions"));
builder.Services.Configure<VectorMemoryOptions>(builder.Configuration.GetSection("VectorMemoryOptions"));
builder.Services.Configure<MemoryPruningOptions>(builder.Configuration.GetSection("MemoryPruningOptions"));

// Register resource limits
var resourceLimits = new ResourceLimits();
builder.Configuration.GetSection("ResourceLimits").Bind(resourceLimits);
builder.Services.AddSingleton(resourceLimits);
builder.Services.AddSingleton<ResourceLimitService>();

// Security and reliability
builder.Services.AddSingleton<ToolAuthorizationService>();
builder.Services.AddSingleton<TelegramBotClientFactory>();

// Metrics and observability
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddSingleton<PerformanceMonitor>();
builder.Services.AddSingleton<DistributedTracing>();

// OpenTelemetry configuration
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "InfernalHierarchy", serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddSource("InfernalHierarchy")
        .AddHttpClientInstrumentation()
        .AddConsoleExporter()
        // Uncomment to export to OTLP (e.g., Jaeger, Zipkin, etc.)
        // .AddOtlpExporter(options => options.Endpoint = new Uri("http://localhost:4317"))
    );

// Resilience policies
builder.Services.AddSingleton<ResiliencePolicies>();
builder.Services.AddSingleton<IResiliencePolicyProvider, ResiliencePolicyProvider>();

// Exception handling
builder.Services.AddSingleton<GlobalExceptionHandler>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<OllamaHealthCheck>("ollama", HealthStatus.Degraded, tags: new[] { "llm", "external" })
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

// Tools - inject IServiceProvider for command handlers
builder.Services.AddSingleton<IToolRegistry>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ToolRegistry>>();
    var learningService = sp.GetRequiredService<AgentLearningService>();
    return new ToolRegistry(logger, learningService, sp);
});
builder.Services.AddSingleton<OllamaClient>();

// Advanced LLM Services
builder.Services.AddSingleton<MultiModelLlmClient>();
builder.Services.AddSingleton<TokenUsageTracker>();
builder.Services.AddSingleton<AgentLearningService>();

// Advanced Memory Services
builder.Services.AddSingleton<OnnxEmbeddingService>();
builder.Services.AddSingleton<VectorMemoryService>();
builder.Services.AddSingleton<ISkillTreeService, SkillTreeService>();
builder.Services.AddHostedService<MemoryPruningService>();

// Agent Collaboration
builder.Services.AddSingleton<IAgentCollaborationService, AgentCollaborationService>();

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
builder.Services.AddSingleton<EventStore>();

// Register search tools
builder.Services.AddHttpClient<SearXNGSearchTool>();
builder.Services.AddHttpClient<BraveSearchTool>();
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

// Register all tools in the registry
builder.Services.AddHostedService<ToolRegistrationService>();

// Configuration validation (runs first)
builder.Services.AddHostedService<ConfigurationValidator>();

// Configuration management
builder.Services.AddHostedService<ConfigurationReloadService>();
builder.Services.AddHostedService<SecretRotationService>();

// Hosted services
builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddHostedService<AgentOrchestrator>();

var host = builder.Build();

try
{
    Log.Information("🔥 InfernalHierarchy starting...");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "💀 Host terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Helper service to register all tools in the registry on startup
/// </summary>
class ToolRegistrationService : IHostedService
{
    private readonly IToolRegistry _registry;
    private readonly IEnumerable<ITool> _tools;
    private readonly ILogger<ToolRegistrationService> _logger;

    public ToolRegistrationService(
        IToolRegistry registry,
        IEnumerable<ITool> tools,
        ILogger<ToolRegistrationService> logger)
    {
        _registry = registry;
        _tools = tools;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 Registering tools...");

        foreach (var tool in _tools)
        {
            _registry.RegisterTool(tool);
        }

        _logger.LogInformation("✅ Registered {Count} tools", _tools.Count());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
