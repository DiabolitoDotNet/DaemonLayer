using InfernalHierarchy.Telegram.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    private readonly ILogger<TelegramOptionsValidator> _logger;

    public TelegramOptionsValidator(ILogger<TelegramOptionsValidator> logger)
    {
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            _logger.LogWarning("⚠️ Telegram:BotToken is not configured. Telegram service will be disabled.");
        }

        if (options.AllowedUserIds.Length == 0)
        {
            _logger.LogWarning(
                "⚠️ Telegram:AllowedUserIds is empty. All users will be able to interact with the bot (not recommended for production).");
        }

        return ValidateOptionsResult.Success;
    }
}
