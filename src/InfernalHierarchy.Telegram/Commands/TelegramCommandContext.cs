using Telegram.Bot;

namespace InfernalHierarchy.Telegram.Commands;

public sealed record TelegramCommandContext(
    ITelegramBotClient BotClient,
    long ChatId,
    string RawText,
    string[] Parts,
    IMessageBus MessageBus,
    ILogger Logger);
