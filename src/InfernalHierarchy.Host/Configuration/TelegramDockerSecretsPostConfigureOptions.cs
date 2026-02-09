using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host.Configuration;

public sealed class TelegramDockerSecretsPostConfigureOptions : IPostConfigureOptions<TelegramOptions>
{
    private const string DefaultDockerSecretsRoot = "/run/secrets";
    private const string BotTokenSecretName = "telegram_bot_token";
    private const string AllowedUserIdsSecretName = "telegram_user_ids";
    private const string LuciferPreambleSecretName = "telegram_lucifer_preamble";

    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramDockerSecretsPostConfigureOptions> _logger;

    public TelegramDockerSecretsPostConfigureOptions(
        IConfiguration configuration,
        ILogger<TelegramDockerSecretsPostConfigureOptions> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void PostConfigure(string? name, TelegramOptions options)
    {
        if (_configuration.GetValue<bool?>("DockerSecrets:Enabled") is false)
        {
            return;
        }

        var rootPath = _configuration["DockerSecrets:RootPath"];
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = DefaultDockerSecretsRoot;
        }

        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            TryLoadBotTokenFromSecret(rootPath, options);
        }

        if (options.AllowedUserIds.Length == 0)
        {
            TryLoadAllowedUserIdsFromSecret(rootPath, options);
        }

        if (string.IsNullOrWhiteSpace(options.LuciferPreamble))
        {
            TryLoadLuciferPreambleFromSecret(rootPath, options);
        }
    }

    private void TryLoadLuciferPreambleFromSecret(string rootPath, TelegramOptions options)
    {
        var path = Path.Combine(rootPath, LuciferPreambleSecretName);

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var preamble = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(preamble))
            {
                _logger.LogWarning("Docker secret {Path} exists but is empty", path);
                return;
            }

            options.LuciferPreamble = preamble;
            _logger.LogInformation("Loaded Telegram Lucifer preamble from docker secret {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Telegram Lucifer preamble from docker secret {Path}", path);
        }
    }

    private void TryLoadBotTokenFromSecret(string rootPath, TelegramOptions options)
    {
        var path = Path.Combine(rootPath, BotTokenSecretName);

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var token = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Docker secret {Path} exists but is empty", path);
                return;
            }

            options.BotToken = token;
            _logger.LogInformation("Loaded Telegram bot token from docker secret {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Telegram bot token from docker secret {Path}", path);
        }
    }

    private void TryLoadAllowedUserIdsFromSecret(string rootPath, TelegramOptions options)
    {
        var path = Path.Combine(rootPath, AllowedUserIdsSecretName);

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var content = File.ReadAllText(path);
            var ids = ParseAllowedUserIds(content);

            if (ids.Length == 0)
            {
                _logger.LogWarning("Docker secret {Path} did not contain any parseable Telegram user ids", path);
                return;
            }

            options.AllowedUserIds = ids;
            _logger.LogInformation("Loaded {Count} Telegram allowed user id(s) from docker secret {Path}", ids.Length, path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Telegram allowed user ids from docker secret {Path}", path);
        }
    }

    private static long[] ParseAllowedUserIds(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<long>();
        }

        var ids = new List<long>();
        var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;

            var parts = line.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (long.TryParse(part.Trim(), out var id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids.Distinct().ToArray();
    }
}
