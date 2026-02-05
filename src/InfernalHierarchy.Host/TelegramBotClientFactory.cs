using Telegram.Bot;
using Telegram.Bot.Types;

namespace InfernalHierarchy.Host;

public interface ITelegramBotClientProbe
{
    Task<User> GetMeAsync(CancellationToken cancellationToken);
}

public interface ITelegramBotClientFactory
{
    ITelegramBotClientProbe Create(string botToken);
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
