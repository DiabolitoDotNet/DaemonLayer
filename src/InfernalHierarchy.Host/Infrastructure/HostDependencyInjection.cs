using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using InfernalHierarchy.Host.Docs;
using InfernalHierarchy.Host.Agents;
using InfernalHierarchy.Host.Migration;
using InfernalHierarchy.Host.Personas;
using InfernalHierarchy.Host.Telegram;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Personas.Loading;
using InfernalHierarchy.Agents.Policies;
using InfernalHierarchy.Messaging.Federation;
using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Tools.GraphQL;
using InfernalHierarchy.Tools.Tools.Notifications;
using InfernalHierarchy.Tools.Tools.Sql;

namespace InfernalHierarchy.Host.Infrastructure;

internal static class HostDependencyInjection
{
    public static void AddSerilogLogging(WebApplicationBuilder builder)
    {
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

        // Avoid duplicate console log output by removing the default providers
        // before adding Serilog as the sole logging pipeline.
        builder.Logging.ClearProviders();

        builder.Host.UseSerilog(Log.Logger, dispose: true);

        builder.Services.AddSingleton(agentContextEnricher);
        builder.Services.AddSingleton(messageContextEnricher);
        builder.Services.AddSingleton(toolContextEnricher);
    }

    public static void AddValidatorsAndPostConfigure(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IValidateOptions<OllamaOptions>, OllamaOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<TelegramOptions>, TelegramOptionsValidator>();
        builder.Services.AddSingleton<IPostConfigureOptions<TelegramOptions>, TelegramDockerSecretsPostConfigureOptions>();
        builder.Services.AddSingleton<IPostConfigureOptions<EmailNotificationOptions>, EmailDockerSecretsPostConfigureOptions>();
        builder.Services.AddSingleton<IPostConfigureOptions<GitHubPublisherOptions>, GitHubPublisherDockerSecretsPostConfigureOptions>();
        builder.Services.AddSingleton<IPostConfigureOptions<BraveSearchOptions>, BraveSearchDockerSecretsPostConfigureOptions>();
        builder.Services.AddSingleton<IValidateOptions<MemoryOptions>, MemoryOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<MemoryBackupOptions>, MemoryBackupOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<HierarchyOptions>, HierarchyOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<SearXNGOptions>, SearXngOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<BraveSearchOptions>, BraveSearchOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<SearXNGOptions>, WebSearchProvidersValidator>();
        builder.Services.AddSingleton<IValidateOptions<BraveSearchOptions>, WebSearchProvidersValidator>();
        builder.Services.AddSingleton<IValidateOptions<EmailNotificationOptions>, EmailNotificationOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<EmailInboxQueryOptions>, EmailInboxQueryOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<ToolRateLimitingOptions>, ToolRateLimitingOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<ToolResultCacheOptions>, ToolResultCacheOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<VectorMemoryOptions>, VectorMemoryOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<OnnxEmbeddingOptions>, OnnxEmbeddingOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<FileSystemToolOptions>, FileSystemToolOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<HttpRequestToolOptions>, HttpRequestToolOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<CodeExecutionToolOptions>, CodeExecutionToolOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<DeliveryWorkflowOptions>, DeliveryWorkflowOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<ToolMarketplaceOptions>, ToolMarketplaceOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<UiInterfaceOptions>, UiInterfaceOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<WebSocketInterfaceOptions>, WebSocketInterfaceOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<VoiceInterfaceOptions>, VoiceInterfaceOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<VoiceTranscriptionToolOptions>, VoiceTranscriptionToolOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<TextToSpeechToolOptions>, TextToSpeechToolOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<VisionToolOptions>, VisionToolOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<AutonomyReadinessOptions>, AutonomyReadinessOptionsValidator>();
    }

    public static void AddResourceLimits(WebApplicationBuilder builder)
    {
        var resourceLimits = HostConfigurationBinding.Read<ResourceLimits>(builder.Configuration, "ResourceLimits");
        builder.Services.AddSingleton(resourceLimits);
        builder.Services.AddSingleton<ResourceLimitService>();
    }

    public static void AddSecurityAndReliability(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ToolAuthorizationService>();
        builder.Services.AddSingleton<IToolAuthorizationService>(sp => sp.GetRequiredService<ToolAuthorizationService>());
        builder.Services.AddSingleton<IAgentPlaygroundService, AgentPlaygroundService>();
        builder.Services.AddSingleton<TelegramBotClientFactory>();
        builder.Services.AddSingleton<ITelegramBotClientFactory>(sp => sp.GetRequiredService<TelegramBotClientFactory>());
        builder.Services.AddSingleton<ITelegramMessageSender, TelegramMessageSender>();

        builder.Services.AddSingleton<ResiliencePolicies>();
        builder.Services.AddSingleton<IResiliencePolicyProvider, ResiliencePolicyProvider>();
        builder.Services.AddSingleton<GlobalExceptionHandler>();
        builder.Services.AddSingleton<IncidentToolThrottleState>();
        builder.Services.AddSingleton<IFailedOperationStore, LiteDbFailedOperationStore>();
        builder.Services.AddSingleton<DeadLetterReplayService>();
        builder.Services.AddHostedService<AutonomousDeadLetterReplayService>();
        builder.Services.AddHostedService<AutonomousIncidentResponseService>();
        builder.Services.AddSingleton<ICapabilityOutcomePublisher, SkillbookOutcomePublisher>();
        builder.Services.AddSingleton<IAgentQuotaService, TenantAgentQuotaService>();
        builder.Services.AddSingleton<IToolExecutionLimiter, ResourceLimitToolExecutionLimiter>();
    }

    public static void AddHealthChecks(WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck<OllamaHealthCheck>("ollama", HealthStatus.Degraded, tags: new[] { "llm", "external" })
            .AddCheck<QdrantHealthCheck>("qdrant", HealthStatus.Degraded, tags: new[] { "vector", "external" })
            .AddCheck<OnnxEmbeddingsHealthCheck>("onnx_embeddings", HealthStatus.Degraded, tags: new[] { "embeddings", "local" })
            .AddCheck<TelegramHealthCheck>("telegram", HealthStatus.Degraded, tags: new[] { "bot", "external" })
            .AddCheck<VoiceSidecarHealthCheck>("voice_sidecar", HealthStatus.Degraded, tags: new[] { "voice", "external" })
            .AddCheck<LiteDbHealthCheck>("litedb", HealthStatus.Unhealthy, tags: new[] { "database", "storage" })
            .AddCheck<AgentHierarchyHealthCheck>("agents", HealthStatus.Degraded, tags: new[] { "agents", "system" });

        builder.Services.AddHttpClient();
    }

    public static void AddObservability(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<MetricsCollector>();
        builder.Services.AddSingleton<MetricsService>();
        builder.Services.AddSingleton<SloGateEvaluator>();
        builder.Services.AddSingleton<AutonomyScorecardService>();
        builder.Services.AddSingleton<AutonomyReadinessReportStore>();
        builder.Services.AddSingleton<OperatorExplainabilityService>();
        builder.Services.AddSingleton<PerformanceMonitor>();
        builder.Services.AddSingleton<DistributedTracing>();
        builder.Services.AddHostedService<MessageBusMetricsReporter>();
        builder.Services.AddHostedService<ActivitySpanProfilingService>();

        builder.Services.AddSingleton<IHttpRequestProfilingStore, InMemoryHttpRequestProfilingStore>();

        builder.Services.AddSingleton<InMemoryTraceCaptureStore>();
        builder.Services.AddSingleton<ITraceCaptureStore>(sp => sp.GetRequiredService<InMemoryTraceCaptureStore>());
        builder.Services.AddHostedService<ActivityTraceCaptureService>();

        var otelExporterOptions = HostConfigurationBinding.ConfigureAndRead<OpenTelemetryExportOptions>(builder, "OpenTelemetry:Exporters");

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
    }

    public static void AddCoreServices(WebApplicationBuilder builder)
    {
        var voiceOptions = HostConfigurationBinding.Read<VoiceInterfaceOptions>(builder.Configuration, "Voice");
        var voiceCopilotOptions = HostConfigurationBinding.Read<VoiceCopilotOptions>(builder.Configuration, "VoiceCopilot");
        var vectorMemoryOptions = HostConfigurationBinding.Read<VectorMemoryOptions>(builder.Configuration, "VectorMemoryOptions");
        var memoryPruningOptions = HostConfigurationBinding.Read<MemoryPruningOptions>(builder.Configuration, "MemoryPruningOptions");
        var memoryLearningOptions = HostConfigurationBinding.Read<MemoryLearningOptions>(builder.Configuration, "MemoryLearningOptions");

        AddMessagingAndMemory(builder);
        AddPersonaAndDocsServices(builder);
        AddAgentSystem(builder);
        AddToolExecutionPipeline(builder);
        AddLlmAndLearning(builder);
        if (voiceOptions.Enabled || voiceCopilotOptions.Enabled)
        {
            AddVoiceCopilot(builder);
        }

        AddNotifications(builder);
        AddAdvancedMemory(builder, vectorMemoryOptions, memoryPruningOptions, memoryLearningOptions);
        AddCollaboration(builder);
        AddFederation(builder);
        AddTemplates(builder);
        AddEventSourcing(builder);
    }

    private static void AddFederation(WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient("FederationServiceClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        builder.Services.AddSingleton<IFederationService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FederationService>>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("FederationServiceClient");
            var collaboration = sp.GetRequiredService<IAgentCollaborationService>();

            var configuredInstanceId = builder.Configuration.GetValue<string>("Federation:LocalInstanceId");
            var localInstanceId = string.IsNullOrWhiteSpace(configuredInstanceId)
                ? $"{Environment.MachineName}-{Environment.ProcessId}"
                : configuredInstanceId.Trim();

            return new FederationService(logger, httpClient, localInstanceId, collaboration);
        });
    }

    private static void AddVoiceCopilot(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<VoiceCopilotService>();

        builder.Services.AddSingleton<VoiceCopilotTtsQueue>();
        builder.Services.AddSingleton<IVoiceCopilotTtsQueue>(sp => sp.GetRequiredService<VoiceCopilotTtsQueue>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<VoiceCopilotTtsQueue>());
    }

    private static void AddMessagingAndMemory(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IMessageBus>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ChannelMessageBus>>();
            var resourceLimits = sp.GetRequiredService<ResourceLimits>();
            var messageBusOptions = sp.GetRequiredService<IOptions<MessageBusOptions>>().Value;

            var queueCapacity = messageBusOptions.QueueCapacity > 0
                ? messageBusOptions.QueueCapacity
                : resourceLimits.MaxMessageQueueSize;

            return new ChannelMessageBus(
                logger,
                queueCapacity,
                messageBusOptions.OverflowPolicy,
                sp.GetService<IFailedOperationStore>(),
                messageBusOptions.Backpressure);
        });
        builder.Services.AddSingleton<LiteDbSharedMemory>();
        builder.Services.AddSingleton<ISharedMemory>(sp => sp.GetRequiredService<LiteDbSharedMemory>());
        builder.Services.AddSingleton<IToolResultCacheStore>(sp => sp.GetRequiredService<LiteDbSharedMemory>());
        builder.Services.AddSingleton<ICustomToolStore>(sp => sp.GetRequiredService<LiteDbSharedMemory>());
        builder.Services.AddSingleton<ITenantIsolationService>(sp =>
            new TenantIsolationService(
                sp.GetRequiredService<ILogger<TenantIsolationService>>(),
                Path.Combine(AppContext.BaseDirectory, "data")));
    }

    private static void AddPersonaAndDocsServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IPersonaLoader, JsonPersonaLoader>();
        builder.Services.AddSingleton<ISkillPackCatalog>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<JsonSkillPackCatalog>>();
            var options = sp.GetRequiredService<IOptions<SkillCatalogOptions>>().Value;
            return new JsonSkillPackCatalog(logger, options.DirectoryPath);
        });
        builder.Services.AddSingleton<IAgentSkillAssignmentPolicy, DefaultAgentSkillAssignmentPolicy>();
        builder.Services.AddSingleton<PersonaFileStore>();
        builder.Services.AddSingleton<DocumentationGenerator>();
        builder.Services.AddSingleton<AgentMigrationService>();
    }

    private static void AddAgentSystem(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AgentRegistry>();
        builder.Services.AddSingleton<IAgentRegistry>(sp => sp.GetRequiredService<AgentRegistry>());
        builder.Services.AddSingleton<IAgentSkillRuntimeStore, LiteDbAgentSkillRuntimeStore>();
        builder.Services.AddHostedService<AgentStatusChangeProjectionService>();
        // ReAct SRP services
        builder.Services.AddSingleton<IActionParser, DefaultActionParser>();
        builder.Services.AddSingleton<IActionInputParser>(sp =>
            new DefaultActionInputParser(sp.GetRequiredService<ILoggerFactory>().CreateLogger("ReAct.ActionInputParser")));
        builder.Services.AddSingleton<IActionExecutor, DefaultActionExecutor>();
        builder.Services.AddSingleton<IReActPromptBuilder, DefaultReActPromptBuilder>();
        builder.Services.AddSingleton<IReActLoopRunner, DefaultReActLoopRunner>();
        builder.Services.AddSingleton<ICapabilityGapAnalyzer, DefaultCapabilityGapAnalyzer>();
        builder.Services.AddSingleton<ICapabilityRemediationOrchestrator, DefaultCapabilityRemediationOrchestrator>();
        builder.Services.AddSingleton<IReportGenerator>(sp =>
            new DefaultReportGenerator(sp.GetService<TokenUsageTracker>(), sp.GetService<MultiModelLlmClient>()));
        builder.Services.AddSingleton<IRagContextEnricher, DefaultRagContextEnricher>();
        builder.Services.AddSingleton<IAgentEventAppender, DefaultAgentEventAppender>();
        builder.Services.AddSingleton<IReActTaskProcessor, DefaultReActTaskProcessor>();
        builder.Services.AddSingleton<IAgentFactory, InfernalHierarchy.Agents.AgentFactory>();
    }

    private static void AddToolExecutionPipeline(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IToolExecutionPipeline, DefaultToolExecutionPipeline>();
        builder.Services.AddSingleton<FixedWindowToolRateLimiter>();
        builder.Services.AddSingleton<IToolRateLimiter, BackpressureAwareToolRateLimiter>();
        builder.Services.AddSingleton<IProcessRunner, DefaultProcessRunner>();
        builder.Services.AddSingleton<IToolPluginLoader, DefaultToolPluginLoader>();
        builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
    }

    private static void AddLlmAndLearning(WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient(nameof(OllamaClient));
        builder.Services.AddSingleton<ILlmClient>(sp =>
            new OllamaClient(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IOptionsMonitor<OllamaOptions>>(),
                sp.GetRequiredService<ILogger<OllamaClient>>(),
                routingFeedback: sp.GetRequiredService<IModelRoutingFeedbackStore>()));

        builder.Services.AddSingleton<IModelRoutingFeedbackStore, InMemoryModelRoutingFeedbackStore>();
        builder.Services.AddSingleton<MultiModelLlmClient>();
        builder.Services.AddSingleton<TokenUsageTracker>();
        builder.Services.AddSingleton<AgentLearningService>();
    }

    private static void AddNotifications(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<SmtpEmailSender>();
        builder.Services.AddSingleton<IEmailSender>(sp =>
            new ResilientEmailSender(
                sp.GetRequiredService<SmtpEmailSender>(),
                sp.GetRequiredService<IResiliencePolicyProvider>(),
                sp.GetRequiredService<ILogger<ResilientEmailSender>>()));
    }

    private static void AddAdvancedMemory(
        WebApplicationBuilder builder,
        VectorMemoryOptions vectorMemoryOptions,
        MemoryPruningOptions memoryPruningOptions,
        MemoryLearningOptions memoryLearningOptions)
    {
        builder.Services.AddSingleton<OnnxEmbeddingService>();
        builder.Services.AddHttpClient<IVectorMemory, VectorMemoryService>();
        builder.Services.AddHostedService<MemoryBackupService>();

        if (vectorMemoryOptions.Enabled)
        {
            builder.Services.AddHostedService<VectorMemoryInitializationService>();
        }

        builder.Services.AddSingleton<ISkillTreeService, SkillTreeService>();

        if (memoryPruningOptions.Enabled)
        {
            builder.Services.AddHostedService<MemoryPruningService>();
        }

        if (memoryLearningOptions.Enabled)
        {
            builder.Services.AddHostedService<MemoryLearningService>();
        }
    }

    private static void AddCollaboration(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IAgentCollaborationService, AgentCollaborationService>();
        builder.Services.AddSingleton<IAggregationStrategy, VotingAggregationStrategy>();
        builder.Services.AddSingleton<IAggregationStrategy, WeightedVotingAggregationStrategy>();
        builder.Services.AddSingleton<IAggregationStrategy, ConsensusAggregationStrategy>();
        builder.Services.AddSingleton<IAggregationStrategy, HighestConfidenceAggregationStrategy>();
        builder.Services.AddSingleton<IAggregationStrategy, HierarchicalAggregationStrategy>();
    }

    private static void AddTemplates(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ITemplateService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TemplateService>>();
            var agentFactory = sp.GetRequiredService<IAgentFactory>();
            var skillTreeService = sp.GetRequiredService<ISkillTreeService>();
            var configuredPath = builder.Configuration.GetValue<string>("Templates:RootPath");
            var templatesDirectory = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(builder.Environment.ContentRootPath, "templates")
                : (Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.Combine(builder.Environment.ContentRootPath, configuredPath));
            return new TemplateService(logger, agentFactory, skillTreeService, templatesDirectory);
        });
    }

    private static void AddEventSourcing(WebApplicationBuilder builder)
    {
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
        builder.Services.AddSingleton<IAgentEventSink, CapabilityGapMetricsEventSink>();
    }

    public static void AddTools(WebApplicationBuilder builder)
    {
        AddSearchToolClients(builder);
        AddWebSearchTools(builder);
        AddToolHttpClients(builder);
        AddToolImplementations(builder);
        AddToolHostedServices(builder);
    }

    private static void AddSearchToolClients(WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient<ISearXngClient, SearXngClient>();
        builder.Services.AddHttpClient<IBraveSearchClient, BraveSearchClient>();
    }

    private static void AddWebSearchTools(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<SearXNGSearchTool>();
        builder.Services.AddSingleton<BraveSearchTool>();
        builder.Services.AddSingleton<WebSearchTool>();

        builder.Services.AddSingleton<IWebSearchTool>(sp => sp.GetRequiredService<WebSearchTool>());
        builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<WebSearchTool>());
    }

    private static void AddToolHttpClients(WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient(nameof(HttpRequestTool));
        builder.Services.AddHttpClient(nameof(GraphQlRequestTool));
        builder.Services.AddHttpClient(nameof(PublishCustomToolsToGitHubTool));
    }

    private static void AddToolImplementations(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ICustomToolCompiler, RoslynCustomToolCompiler>();
        builder.Services.AddSingleton<ICustomToolSecurityPolicy, DefaultCustomToolSecurityPolicy>();

        builder.Services.AddSingleton<ITool, CreateSubAgentTool>();
        builder.Services.AddSingleton<ITool, GetAgentStatusTool>();
        builder.Services.AddSingleton<ITool, MemoryReadTool>();
        builder.Services.AddSingleton<ITool, MemoryWriteTool>();
        builder.Services.AddSingleton<ITool, RequestCollaborationTool>();
        builder.Services.AddSingleton<ITool, TelegramSendTool>();
        builder.Services.AddSingleton<ITool, CreateCustomToolTool>();
        builder.Services.AddSingleton<ITool, GetCustomToolSourceTool>();
        builder.Services.AddSingleton<ITool, ListCustomToolsTool>();
        builder.Services.AddSingleton<ITool, DeleteCustomToolTool>();
        builder.Services.AddSingleton<ITool, PublishCustomToolsToGitHubTool>();
        builder.Services.AddSingleton<ITool, CreateAgentFromTemplateTool>();
        builder.Services.AddSingleton<ITool, ListTemplatesTool>();
        builder.Services.AddSingleton<ITool, PromptAbTestTool>();
        builder.Services.AddSingleton<ITool, SendAgentMessageTool>();
        builder.Services.AddSingleton<ITool, RequestSkillPackTool>();
        builder.Services.AddSingleton<ITool, EmailNotificationTool>();
        builder.Services.AddSingleton<IEmailInboxQueryClient, ImapEmailInboxQueryClient>();
        builder.Services.AddSingleton<ITool, EmailInboxQueryTool>();
        builder.Services.AddSingleton<ITool, FileReadTool>();
        builder.Services.AddSingleton<ITool, FileWriteTool>();
        builder.Services.AddSingleton<ITool, FileSearchTool>();
        builder.Services.AddSingleton<ITool, HttpRequestTool>();
        builder.Services.AddSingleton<ITool, GraphQlRequestTool>();
        builder.Services.AddSingleton<ITool, SqlReadOnlyQueryTool>();
        builder.Services.AddSingleton<ITool, PythonExecTool>();
        builder.Services.AddSingleton<ITool, NodeExecTool>();
        builder.Services.AddSingleton<ITool, RepoAnalyzeTool>();
        builder.Services.AddSingleton<ITool, WorkflowStepTool>();
        builder.Services.AddSingleton<ITool, DeployAdapterTool>();
        builder.Services.AddSingleton<ITool, AudioTranscribeTool>();
        builder.Services.AddSingleton<ITool, TextToSpeechTool>();
        builder.Services.AddSingleton<ITool, VisionDescribeTool>();
    }

    private static void AddToolHostedServices(WebApplicationBuilder builder)
    {
        var toolMarketplaceOptions = HostConfigurationBinding.Read<ToolMarketplaceOptions>(builder.Configuration, "ToolMarketplace");

        builder.Services.AddHostedService<TextToSpeechWarmupService>();
        builder.Services.AddHostedService<ToolCacheStartupService>();
        builder.Services.AddHostedService<ToolRegistrationService>();
        builder.Services.AddHostedService<CustomToolsStartupService>();

        if (toolMarketplaceOptions.Enabled)
        {
            builder.Services.AddHostedService<ToolMarketplaceHostedService>();
        }
    }

    public static void AddConfigurationHostedServices(WebApplicationBuilder builder)
    {
        builder.Services.AddHostedService<ConfigurationReloadService>();
        builder.Services.AddHostedService<SecretRotationService>();
        builder.Services.AddHostedService<StartupFeatureReportService>();
        builder.Services.AddHostedService<AutonomyReadinessHostedService>();
    }

    public static void AddHostedServices(WebApplicationBuilder builder)
    {
        builder.Services.AddInfernalTelegramCommandHandlers();
        builder.Services.AddHostedService<TelegramBotService>();
        builder.Services.AddHostedService<AgentOrchestrator>();
        builder.Services.AddHostedService<FederationHealthMonitorHostedService>();

        builder.Services.AddSingleton<IAgentSupervisor, AgentSupervisor>();
        builder.Services.AddHostedService(sp => (AgentSupervisor)sp.GetRequiredService<IAgentSupervisor>());
    }
}
