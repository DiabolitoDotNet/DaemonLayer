using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace InfernalHierarchy.Host.Telegram;

public interface ITelegramBotClientProbe
{
    Task<User> GetMeAsync(CancellationToken cancellationToken);
}

public interface ITelegramBotClientFactory
{
    ITelegramBotClientProbe Create(string botToken);
}

public sealed class TelegramBotClientFactory : ITelegramBotClientFactory
{
    private readonly ILogger<TelegramBotClientFactory> _logger;
    private TelegramBotClient? _client;
    private readonly object _lock = new();

    public TelegramBotClientFactory(ILogger<TelegramBotClientFactory> logger)
    {
        _logger = logger;
    }

    public ITelegramBotClientProbe Create(string botToken)
    {
        return new TelegramBotClientProbe(GetOrCreateClient(botToken));
    }

    internal TelegramBotClient GetOrCreateClient(string botToken)
    {
        lock (_lock)
        {
            if (_client == null)
            {
                _client = new TelegramBotClient(botToken);
                _logger.LogInformation("📱 Telegram bot client created");
            }

            return _client;
        }
    }

    public void RecreateClient(string newBotToken)
    {
        lock (_lock)
        {
            _client = new TelegramBotClient(newBotToken);
            _logger.LogInformation("🔄 Telegram bot client recreated with new token");
        }
    }

    internal TelegramBotClient? Client
    {
        get
        {
            lock (_lock)
            {
                return _client;
            }
        }
    }
}

internal sealed class TelegramBotClientProbe : ITelegramBotClientProbe
{
    private readonly Func<CancellationToken, Task<User>> _getMeAsync;

    public TelegramBotClientProbe(ITelegramBotClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _getMeAsync = ct => client.GetMe(ct);
    }

    internal TelegramBotClientProbe(Func<CancellationToken, Task<User>> getMeAsync)
    {
        ArgumentNullException.ThrowIfNull(getMeAsync);
        _getMeAsync = getMeAsync;
    }

    public Task<User> GetMeAsync(CancellationToken cancellationToken)
    {
        return _getMeAsync(cancellationToken);
    }
}
