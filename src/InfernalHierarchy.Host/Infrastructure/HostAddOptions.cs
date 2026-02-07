namespace InfernalHierarchy.Host.Infrastructure;

internal static class HostAddOptions
{
    public static void AddValidatedOptions(WebApplicationBuilder builder)
    {
        AddAgentAndCoreOptions(builder);
        AddSearchAndProviderOptions(builder);
        AddToolOptions(builder);
        AddCritiqueOptions(builder);
        AddUiAndInterfaceOptions(builder);
        AddVectorMemoryOptions(builder);
        AddOperatorApiOptions(builder);
        AddSupervisorOptions(builder);
    }

    private static void AddCritiqueOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<CritiqueOptions>()
            .Bind(builder.Configuration.GetSection("Critique"))
            .ValidateOnStart();
    }

    private static void AddAgentAndCoreOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<OllamaOptions>()
            .Bind(builder.Configuration.GetSection("Ollama"))
            .ValidateOnStart();
        builder.Services.AddOptions<TelegramOptions>()
            .Bind(builder.Configuration.GetSection("Telegram"))
            .ValidateOnStart();
        builder.Services.AddOptions<TelegramVoiceOptions>()
            .Bind(builder.Configuration.GetSection("TelegramVoice"))
            .ValidateOnStart();
        builder.Services.AddOptions<MemoryOptions>()
            .Bind(builder.Configuration.GetSection("Memory"))
            .ValidateOnStart();
        builder.Services.AddOptions<HierarchyOptions>()
            .Bind(builder.Configuration.GetSection("Hierarchy"))
            .ValidateOnStart();
    }

    private static void AddSearchAndProviderOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<SearXNGOptions>()
            .Bind(builder.Configuration.GetSection("SearXNG"))
            .ValidateOnStart();
        builder.Services.AddOptions<BraveSearchOptions>()
            .Bind(builder.Configuration.GetSection("BraveSearch"))
            .ValidateOnStart();
        builder.Services.AddOptions<EmailNotificationOptions>()
            .Bind(builder.Configuration.GetSection("Email"))
            .ValidateOnStart();
    }

    private static void AddToolOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ToolRateLimitingOptions>()
            .Bind(builder.Configuration.GetSection("ToolRateLimiting"))
            .ValidateOnStart();
        builder.Services.AddOptions<ToolResultCacheOptions>()
            .Bind(builder.Configuration.GetSection("ToolCache"))
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
    }

    private static void AddUiAndInterfaceOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<UiInterfaceOptions>()
            .Bind(builder.Configuration.GetSection("Ui"))
            .ValidateOnStart();
        builder.Services.AddOptions<WebSocketInterfaceOptions>()
            .Bind(builder.Configuration.GetSection("WebSockets"))
            .ValidateOnStart();
        builder.Services.AddOptions<VoiceInterfaceOptions>()
            .Bind(builder.Configuration.GetSection("Voice"))
            .ValidateOnStart();
        builder.Services.AddOptions<VoiceTranscriptionToolOptions>()
            .Bind(builder.Configuration.GetSection("VoiceTranscription"))
            .ValidateOnStart();
        builder.Services.AddOptions<TextToSpeechToolOptions>()
            .Bind(builder.Configuration.GetSection("TextToSpeech"))
            .ValidateOnStart();
    }

    private static void AddVectorMemoryOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<VectorMemoryOptions>()
            .Bind(builder.Configuration.GetSection("VectorMemoryOptions"))
            .ValidateOnStart();
        builder.Services.AddOptions<OnnxEmbeddingOptions>()
            .Bind(builder.Configuration.GetSection("OnnxEmbeddingOptions"))
            .ValidateOnStart();
    }

    private static void AddOperatorApiOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<OperatorApiOptions>()
            .Bind(builder.Configuration.GetSection("OperatorApi"))
            .ValidateOnStart();
    }

    private static void AddSupervisorOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<AgentSupervisorOptions>()
            .Bind(builder.Configuration.GetSection("AgentSupervisor"))
            .ValidateOnStart();
    }
}
