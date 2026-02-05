using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Host.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Security;

/// <summary>
/// Service that monitors and rotates secrets without requiring application restart
/// </summary>
public class SecretRotationService : BackgroundService
{
    private readonly ILogger<SecretRotationService> _logger;
    private readonly IOptionsMonitor<TelegramOptions> _telegramOptions;
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<BraveSearchOptions> _braveOptions;
    private readonly IConfiguration _configuration;
    private readonly TelegramBotClientFactory _botFactory;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    private string? _lastTelegramToken;
    private Uri? _lastOllamaUrl;
    private string? _lastBraveApiKey;

    public SecretRotationService(
        ILogger<SecretRotationService> logger,
        IOptionsMonitor<TelegramOptions> telegramOptions,
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<BraveSearchOptions> braveOptions,
        IConfiguration configuration,
        TelegramBotClientFactory botFactory)
    {
        _logger = logger;
        _telegramOptions = telegramOptions;
        _ollamaOptions = ollamaOptions;
        _braveOptions = braveOptions;
        _configuration = configuration;
        _botFactory = botFactory;

        // Store initial values
        _lastTelegramToken = _telegramOptions.CurrentValue.BotToken;
        _lastOllamaUrl = _ollamaOptions.CurrentValue.BaseUrl;
        _lastBraveApiKey = _braveOptions.CurrentValue.ApiKey;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔐 Secret rotation service started");

        // Register change callbacks
        _telegramOptions.OnChange(OnTelegramOptionsChanged);
        _ollamaOptions.OnChange(OnOllamaOptionsChanged);
        _braveOptions.OnChange(OnBraveOptionsChanged);

        // Periodic check for configuration changes
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);

                // Check if configuration file was reloaded
                _logger.LogDebug("🔄 Checking for configuration changes...");

                // The IOptionsMonitor will automatically trigger OnChange callbacks
                // This loop just keeps the service alive
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in secret rotation service");
            }
        }

        _logger.LogInformation("🔐 Secret rotation service stopped");
    }

    private void OnTelegramOptionsChanged(TelegramOptions newOptions, string? name)
    {
        if (newOptions.BotToken != _lastTelegramToken && !string.IsNullOrEmpty(newOptions.BotToken))
        {
            _logger.LogWarning("🔄 Telegram bot token changed - recreating client");

            try
            {
                _botFactory.RecreateClient(newOptions.BotToken);
                _lastTelegramToken = newOptions.BotToken;

                _logger.LogInformation("✅ Telegram bot client updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update Telegram bot client");
            }
        }
    }

    private void OnOllamaOptionsChanged(OllamaOptions newOptions, string? name)
    {
        if (newOptions.BaseUrl != _lastOllamaUrl && newOptions.BaseUrl is not null)
        {
            _logger.LogWarning("🔄 Ollama base URL changed: {OldUrl} → {NewUrl}", _lastOllamaUrl?.ToString() ?? "null", newOptions.BaseUrl.ToString());
            _lastOllamaUrl = newOptions.BaseUrl;

            // Note: OllamaClient should use IOptionsMonitor to pick up changes automatically
            _logger.LogInformation("✅ Ollama configuration updated (will apply on next request)");
        }
    }

    private void OnBraveOptionsChanged(BraveSearchOptions newOptions, string? name)
    {
        if (newOptions.ApiKey != _lastBraveApiKey && !string.IsNullOrEmpty(newOptions.ApiKey))
        {
            _logger.LogWarning("🔄 Brave Search API key changed");
            _lastBraveApiKey = newOptions.ApiKey;

            _logger.LogInformation("✅ Brave Search configuration updated (will apply on next request)");
        }
    }
}
