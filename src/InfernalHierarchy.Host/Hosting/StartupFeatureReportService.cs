namespace InfernalHierarchy.Host.Hosting;

internal sealed class StartupFeatureReportService : IHostedService
{
    private readonly IOptions<HttpEndpointOptions> _httpOptions;
    private readonly IOptions<UiInterfaceOptions> _uiOptions;
    private readonly IOptions<WebSocketInterfaceOptions> _webSocketOptions;
    private readonly IOptions<VoiceInterfaceOptions> _voiceOptions;
    private readonly IOptions<VoiceCopilotOptions> _voiceCopilotOptions;
    private readonly IOptions<VectorMemoryOptions> _vectorMemoryOptions;
    private readonly IOptions<MemoryPruningOptions> _memoryPruningOptions;
    private readonly IOptions<MemoryLearningOptions> _memoryLearningOptions;
    private readonly IOptions<ToolMarketplaceOptions> _toolMarketplaceOptions;
    private readonly IOptions<ToolResultCacheOptions> _toolCacheOptions;
    private readonly IOptions<OpenTelemetryExportOptions> _openTelemetryOptions;
    private readonly ILogger<StartupFeatureReportService> _logger;

    public StartupFeatureReportService(
        IOptions<HttpEndpointOptions> httpOptions,
        IOptions<UiInterfaceOptions> uiOptions,
        IOptions<WebSocketInterfaceOptions> webSocketOptions,
        IOptions<VoiceInterfaceOptions> voiceOptions,
        IOptions<VoiceCopilotOptions> voiceCopilotOptions,
        IOptions<VectorMemoryOptions> vectorMemoryOptions,
        IOptions<MemoryPruningOptions> memoryPruningOptions,
        IOptions<MemoryLearningOptions> memoryLearningOptions,
        IOptions<ToolMarketplaceOptions> toolMarketplaceOptions,
        IOptions<ToolResultCacheOptions> toolCacheOptions,
        IOptions<OpenTelemetryExportOptions> openTelemetryOptions,
        ILogger<StartupFeatureReportService> logger)
    {
        _httpOptions = httpOptions;
        _uiOptions = uiOptions;
        _webSocketOptions = webSocketOptions;
        _voiceOptions = voiceOptions;
        _voiceCopilotOptions = voiceCopilotOptions;
        _vectorMemoryOptions = vectorMemoryOptions;
        _memoryPruningOptions = memoryPruningOptions;
        _memoryLearningOptions = memoryLearningOptions;
        _toolMarketplaceOptions = toolMarketplaceOptions;
        _toolCacheOptions = toolCacheOptions;
        _openTelemetryOptions = openTelemetryOptions;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var http = _httpOptions.Value;
        var ui = _uiOptions.Value;
        var webSockets = _webSocketOptions.Value;
        var voice = _voiceOptions.Value;
        var voiceCopilot = _voiceCopilotOptions.Value;
        var vectorMemory = _vectorMemoryOptions.Value;
        var memoryPruning = _memoryPruningOptions.Value;
        var memoryLearning = _memoryLearningOptions.Value;
        var toolMarketplace = _toolMarketplaceOptions.Value;
        var toolCache = _toolCacheOptions.Value;
        var openTelemetry = _openTelemetryOptions.Value;

        _logger.LogInformation(
            "Startup features | http={HttpEnabled} urls={HttpUrls} ui={UiEnabled} ws={WebSocketsEnabled} voice={VoiceEnabled} voice_copilot={VoiceCopilotEnabled} vector_memory={VectorMemoryEnabled} memory_pruning={MemoryPruningEnabled} memory_learning={MemoryLearningEnabled} tool_marketplace={ToolMarketplaceEnabled} tool_cache={ToolCacheEnabled} tool_cache_clear_on_startup={ToolCacheClearOnStartup} otel_console={OtelConsoleEnabled} otel_otlp={OtelOtlpEnabled}",
            http.Enabled,
            http.Urls,
            ui.Enabled,
            webSockets.Enabled,
            voice.Enabled,
            voiceCopilot.Enabled,
            vectorMemory.Enabled,
            memoryPruning.Enabled,
            memoryLearning.Enabled,
            toolMarketplace.Enabled,
            toolCache.Enabled,
            toolCache.ClearOnStartup,
            openTelemetry.Console.Enabled,
            openTelemetry.Otlp.Enabled);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}