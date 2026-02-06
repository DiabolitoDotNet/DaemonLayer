using InfernalHierarchy.Agents.Collaboration;
using InfernalHierarchy.Agents.Collaboration.Strategies;
using InfernalHierarchy.Agents.Factory;
using InfernalHierarchy.Agents.Orchestration;
using InfernalHierarchy.Agents.Registry;
using InfernalHierarchy.Agents.Templates;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core.Configuration;
using InfernalHierarchy.Core.Entities;
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
using InfernalHierarchy.Tools.Marketplace;
using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Telemetry;
using InfernalHierarchy.Tools.Tools.Agent;
using InfernalHierarchy.Tools.Tools.Collaboration;
using InfernalHierarchy.Tools.Tools.Experiments;
using InfernalHierarchy.Tools.Tools.FileSystem;
using InfernalHierarchy.Tools.Tools.Http;
using InfernalHierarchy.Tools.Tools.CodeExecution;
using InfernalHierarchy.Tools.Tools.Memory;
using InfernalHierarchy.Tools.Tools.Search;
using InfernalHierarchy.Tools.Tools.Telegram;
using InfernalHierarchy.Tools.Tools.Templates;
using InfernalHierarchy.Tools.Tools.Notifications;
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
using InfernalHierarchy.Host.Api;

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
builder.Services.AddSingleton<IPostConfigureOptions<TelegramOptions>, TelegramDockerSecretsPostConfigureOptions>();
builder.Services.AddSingleton<IPostConfigureOptions<EmailNotificationOptions>, EmailDockerSecretsPostConfigureOptions>();
builder.Services.AddSingleton<IValidateOptions<MemoryOptions>, MemoryOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<HierarchyOptions>, HierarchyOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<SearXNGOptions>, SearXngOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<BraveSearchOptions>, BraveSearchOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<SearXNGOptions>, WebSearchProvidersValidator>();
builder.Services.AddSingleton<IValidateOptions<BraveSearchOptions>, WebSearchProvidersValidator>();
builder.Services.AddSingleton<IValidateOptions<EmailNotificationOptions>, EmailNotificationOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<ToolRateLimitingOptions>, ToolRateLimitingOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<VectorMemoryOptions>, VectorMemoryOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<OnnxEmbeddingOptions>, OnnxEmbeddingOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<FileSystemToolOptions>, FileSystemToolOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<HttpRequestToolOptions>, HttpRequestToolOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<CodeExecutionToolOptions>, CodeExecutionToolOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<ToolMarketplaceOptions>, ToolMarketplaceOptionsValidator>();

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
builder.Services.AddOptions<EmailNotificationOptions>()
    .Bind(builder.Configuration.GetSection("Email"))
    .ValidateOnStart();
builder.Services.AddOptions<ToolRateLimitingOptions>()
    .Bind(builder.Configuration.GetSection("ToolRateLimiting"))
    .ValidateOnStart();
builder.Services.AddOptions<FileSystemToolOptions>()
    .Bind(builder.Configuration.GetSection("FileSystem"))
    .ValidateOnStart();
builder.Services.AddOptions<HttpRequestToolOptions>()
    .Bind(builder.Configuration.GetSection("HttpTool"))
    .ValidateOnStart();
builder.Services.AddOptions<CodeExecutionToolOptions>()
    .Bind(builder.Configuration.GetSection("CodeExecution"))
    .ValidateOnStart();
builder.Services.AddOptions<ToolMarketplaceOptions>()
    .Bind(builder.Configuration.GetSection("ToolMarketplace"))
    .ValidateOnStart();
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("LlmOptions"));
builder.Services.AddOptions<VectorMemoryOptions>()
    .Bind(builder.Configuration.GetSection("VectorMemoryOptions"))
    .ValidateOnStart();
builder.Services.AddOptions<OnnxEmbeddingOptions>()
    .Bind(builder.Configuration.GetSection("OnnxEmbeddingOptions"))
    .ValidateOnStart();
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
builder.Services.AddSingleton<IToolAuthorizationService>(sp => sp.GetRequiredService<ToolAuthorizationService>());
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
    .AddCheck<OnnxEmbeddingsHealthCheck>("onnx_embeddings", HealthStatus.Degraded, tags: new[] { "embeddings", "local" })
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

// Tool rate limiting
builder.Services.AddSingleton<IToolRateLimiter, FixedWindowToolRateLimiter>();

// Process execution (for code execution tools)
builder.Services.AddSingleton<IProcessRunner, DefaultProcessRunner>();

// Tool marketplace (plugin loader)
builder.Services.AddSingleton<IToolPluginLoader, DefaultToolPluginLoader>();

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

// Notifications
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

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

// Register HTTP tool client
builder.Services.AddHttpClient(nameof(HttpRequestTool));

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
builder.Services.AddSingleton<ITool, EmailNotificationTool>();
builder.Services.AddSingleton<ITool, FileReadTool>();
builder.Services.AddSingleton<ITool, FileWriteTool>();
builder.Services.AddSingleton<ITool, FileSearchTool>();
builder.Services.AddSingleton<ITool, HttpRequestTool>();
builder.Services.AddSingleton<ITool, PythonExecTool>();
builder.Services.AddSingleton<ITool, NodeExecTool>();

// Register all tools in the registry
builder.Services.AddHostedService<ToolRegistrationService>();

// Hot-load external tool plugins
builder.Services.AddHostedService<ToolMarketplaceHostedService>();

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

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check =>
            check.Tags.Contains("external") ||
            check.Tags.Contains("database") ||
            check.Tags.Contains("storage") ||
            check.Tags.Contains("embeddings"),
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

    app.MapPost("/api/chat", async (
        ChatRequest request,
        IMessageBus messageBus,
        CancellationToken ct) =>
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "Missing request body: message" });
        }

        if (request.Message.Length > 10_000)
        {
            return Results.BadRequest(new { error = "Message too long (max 10000 chars)" });
        }

        var toAgentId = string.IsNullOrWhiteSpace(request.ToAgentId)
            ? "lucifer"
            : request.ToAgentId.Trim();

        var timeoutMs = request.TimeoutMs is > 0 and <= 300_000
            ? request.TimeoutMs.Value
            : 180_000;

        var replyToId = $"http-{Guid.NewGuid():N}";
        var startedUtc = DateTime.UtcNow;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        var enumerator = messageBus.SubscribeAsync(replyToId, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);

        try
        {
            var message = new AgentMessage
            {
                FromAgentId = replyToId,
                ToAgentId = toAgentId,
                Type = MessageType.Task,
                Content = request.Message,
                Payload = new Dictionary<string, object>
                {
                    ["transport"] = "http",
                    ["http_request_id"] = replyToId,
                    ["http_started_utc"] = startedUtc.ToString("O")
                }
            };

            await messageBus.PublishAsync(message, ct);

            while (await enumerator.MoveNextAsync())
            {
                var response = enumerator.Current;

                // Prefer the agent's final report; ignore other message types if any.
                if (response.Type != MessageType.Report)
                {
                    continue;
                }

                return Results.Ok(new ChatResponse(
                    fromAgentId: response.FromAgentId,
                    toAgentId: response.ToAgentId,
                    content: response.Content,
                    payload: response.Payload,
                    receivedUtc: DateTime.UtcNow,
                    durationMs: (DateTime.UtcNow - startedUtc).TotalMilliseconds));
            }

            return Results.Problem(
                title: "Timeout",
                detail: $"No report received within {timeoutMs}ms",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return Results.Problem(
                title: "Timeout",
                detail: $"No report received within {timeoutMs}ms",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        finally
        {
            await enumerator.DisposeAsync();
            if (messageBus is ChannelMessageBus cmb)
            {
                cmb.CleanupAgent(replyToId);
            }
        }
    });
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
