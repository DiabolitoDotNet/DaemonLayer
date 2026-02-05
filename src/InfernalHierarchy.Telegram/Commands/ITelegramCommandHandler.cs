using Telegram.Bot;

namespace InfernalHierarchy.Telegram.Commands;

public interface ITelegramCommandHandler
{
    string Command { get; }

    Task HandleAsync(TelegramCommandContext context, CancellationToken ct);
}
