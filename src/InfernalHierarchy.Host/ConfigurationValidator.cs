using InfernalHierarchy.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host;

/// <summary>
/// Validates all configuration on startup and provides clear error messages
/// </summary>
public class ConfigurationValidator : IHostedService
{
    private readonly ILogger<ConfigurationValidator> _logger;
    private readonly OllamaOptions _ollamaOptions;
    private readonly TelegramOptions _telegramOptions;
    private readonly MemoryOptions _memoryOptions;
    private readonly HierarchyOptions _hierarchyOptions;
    private readonly SearXNGOptions _searxngOptions;
    private readonly BraveSearchOptions _braveOptions;

    public ConfigurationValidator(
        IOptions<OllamaOptions> ollamaOptions,
        IOptions<TelegramOptions> telegramOptions,
        IOptions<MemoryOptions> memoryOptions,
        IOptions<HierarchyOptions> hierarchyOptions,
        IOptions<SearXNGOptions> searxngOptions,
        IOptions<BraveSearchOptions> braveOptions,
        ILogger<ConfigurationValidator> logger)
    {
        _ollamaOptions = ollamaOptions.Value;
        _telegramOptions = telegramOptions.Value;
        _memoryOptions = memoryOptions.Value;
        _hierarchyOptions = hierarchyOptions.Value;
        _searxngOptions = searxngOptions.Value;
        _braveOptions = braveOptions.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔍 Validating configuration...");

        var errors = new List<string>();

        // Validate Ollama configuration
        if (string.IsNullOrWhiteSpace(_ollamaOptions.BaseUrl))
        {
            errors.Add("Ollama:BaseUrl is required");
        }
        else if (!Uri.TryCreate(_ollamaOptions.BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add($"Ollama:BaseUrl is not a valid URL: {_ollamaOptions.BaseUrl}");
        }

        if (string.IsNullOrWhiteSpace(_ollamaOptions.DefaultModel))
        {
            errors.Add("Ollama:DefaultModel is required");
        }

        if (_ollamaOptions.MaxTokens <= 0)
        {
            errors.Add("Ollama:MaxTokens must be greater than 0");
        }

        if (_ollamaOptions.Temperature < 0 || _ollamaOptions.Temperature > 2)
        {
            _logger.LogWarning("⚠️ Ollama:Temperature {Temp} is outside recommended range 0-2", _ollamaOptions.Temperature);
        }

        // Validate Telegram configuration
        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken))
        {
            _logger.LogWarning("⚠️ Telegram:BotToken is not configured. Telegram service will be disabled.");
        }

        if (_telegramOptions.AllowedUserIds.Length == 0)
        {
            _logger.LogWarning("⚠️ Telegram:AllowedUserIds is empty. All users will be able to interact with the bot (not recommended for production).");
        }

        // Validate Memory configuration
        if (string.IsNullOrWhiteSpace(_memoryOptions.DatabasePath))
        {
            errors.Add("Memory:DatabasePath is required");
        }
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_memoryOptions.DatabasePath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                _logger.LogInformation("📁 Creating memory directory: {Directory}", directory);
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    errors.Add($"Cannot create memory directory {directory}: {ex.Message}");
                }
            }
        }

        // Validate Hierarchy configuration
        if (string.IsNullOrWhiteSpace(_hierarchyOptions.MainAgentName))
        {
            errors.Add("Hierarchy:MainAgentName is required");
        }

        if (string.IsNullOrWhiteSpace(_hierarchyOptions.MainAgentPersonaPath))
        {
            errors.Add("Hierarchy:MainAgentPersonaPath is required");
        }
        else
        {
            var personaPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", _hierarchyOptions.MainAgentPersonaPath);
            personaPath = Path.GetFullPath(personaPath);

            if (!File.Exists(personaPath))
            {
                errors.Add($"Main agent persona file not found: {personaPath}");
            }
        }

        if (_hierarchyOptions.MaxAgentDepth <= 0)
        {
            errors.Add("Hierarchy:MaxAgentDepth must be greater than 0");
        }
        else if (_hierarchyOptions.MaxAgentDepth > 10)
        {
            _logger.LogWarning("⚠️ Hierarchy:MaxAgentDepth {Depth} is very high. This may cause performance issues.", _hierarchyOptions.MaxAgentDepth);
        }

        // Validate Search configuration
        if (!_searxngOptions.Enabled && !_braveOptions.Enabled)
        {
            _logger.LogWarning("⚠️ Both SearXNG and Brave Search are disabled. Web search functionality will not work.");
        }

        if (_searxngOptions.Enabled)
        {
            if (string.IsNullOrWhiteSpace(_searxngOptions.BaseUrl))
            {
                errors.Add("SearXNG:BaseUrl is required when SearXNG is enabled");
            }
            else if (!Uri.TryCreate(_searxngOptions.BaseUrl, UriKind.Absolute, out _))
            {
                errors.Add($"SearXNG:BaseUrl is not a valid URL: {_searxngOptions.BaseUrl}");
            }
        }

        if (_braveOptions.Enabled && string.IsNullOrWhiteSpace(_braveOptions.ApiKey))
        {
            _logger.LogWarning("⚠️ BraveSearch:ApiKey is not configured. Brave Search fallback will not work.");
        }

        // Report results
        if (errors.Any())
        {
            _logger.LogError("❌ Configuration validation failed with {Count} error(s):", errors.Count);
            foreach (var error in errors)
            {
                _logger.LogError("  - {Error}", error);
            }
            throw new InvalidOperationException($"Configuration validation failed. Fix the {errors.Count} error(s) above and restart.");
        }

        _logger.LogInformation("✅ Configuration validated successfully");

        // Log configuration summary
        _logger.LogInformation("📋 Configuration Summary:");
        _logger.LogInformation("  - Ollama: {Url} (Model: {Model})", _ollamaOptions.BaseUrl, _ollamaOptions.DefaultModel);
        _logger.LogInformation("  - Telegram: {Status}", string.IsNullOrWhiteSpace(_telegramOptions.BotToken) ? "Disabled" : "Enabled");
        _logger.LogInformation("  - Memory: {Path}", _memoryOptions.DatabasePath);
        _logger.LogInformation("  - Main Agent: {Name} ({Persona})", _hierarchyOptions.MainAgentName, _hierarchyOptions.MainAgentPersonaPath);
        _logger.LogInformation("  - Web Search: SearXNG={SearXNG}, Brave={Brave}", _searxngOptions.Enabled, _braveOptions.Enabled);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
